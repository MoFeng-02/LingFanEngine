using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.AI;

/// <summary>
/// 共享的批量翻译执行器——把 "多文本 → 批量请求 → 解析 → 占位符校验 → 自校正" 收敛到一处。
/// <para>OpenAI 兼容 / Anthropic 客户端都经此执行，杜绝此前 <c>AiTranslator/AnthropicTranslator</c> 95% 重复。</para>
/// </summary>
public static class TranslationBatchExecutor
{
    /// <summary>喂给模型的源文/诊断用宽松 JSON 编码（单例见 <see cref="SdkJsonContext.LenientString"/>）；保留中文可读、省 token。</summary>
    private static JsonTypeInfo<string> s_promptStringInfo => SdkJsonContext.LenientString;

    /// <summary>估算批量输出上限：约 250 token/条，夹在 [1024, 8192] 防止无界截断或超出模型上限。</summary>
    private static int EstimateMaxTokens(int count) => Math.Clamp(count * 250, 1024, 8192);
    /// <summary>批量翻译一组文本；按序返回译文数组，失败条为 null。可传 <paramref name="usage"/> 累计每次请求的 tokens。</summary>
    /// <param name="progress">每批翻译完成后的进度回调（实数时反馈：completed/total）。</param>
    public static async Task<IReadOnlyList<string?>> TranslateAsync(
        IModelClient client, ModelAdvancedConfig advanced, IReadOnlyList<string> texts,
        string targetLang, string sourceLang, CancellationToken ct, UsageStats? usage = null,
        IProgress<TranslationProgress>? progress = null)
    {
        var results = new string?[texts.Count];
        if (texts.Count == 0) return results;

        var batchSize = Math.Max(1, advanced.BatchSize);
        progress?.Report(new TranslationProgress(0, texts.Count, "正在连接模型…"));
        for (var start = 0; start < texts.Count; start += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var end = Math.Min(start + batchSize, texts.Count);
            var batch = new List<(int Index, string Text)>();
            for (var i = start; i < end; i++)
                batch.Add((i, texts[i]));

            // 批前：给宿主"正在请求"的实时状态（不再长时间停留在 0/N 无反馈）
            progress?.Report(new TranslationProgress(start, texts.Count, "正在请求翻译…"));
            List<(int Index, string Text)> translated;
            try
            {
                translated = await BatchOnceAsync(client, advanced, batch, targetLang, sourceLang, ct, usage, progress, start, texts.Count).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 宿主主动取消 → 立即停止，不吞
                throw;
            }
            catch
            {
                // 该批请求失败（断网/SSL/超时重试耗尽/服务异常）：不拖垮整轮——
                // 标记该批失败继续后续批次，保住已成功的批次；失败项由调用方回退到原文/留空，
                // 下次同步可增量补齐，避免前面的翻译前功尽弃。
                translated = new List<(int Index, string Text)>();
            }
            foreach (var (idx, text) in translated)
                results[idx] = text;

            progress?.Report(new TranslationProgress(end, texts.Count, ""));
        }
        return results;
    }

    private static async Task<List<(int Index, string Text)>> BatchOnceAsync(
        IModelClient client, ModelAdvancedConfig advanced, List<(int Index, string Text)> batch,
        string targetLang, string sourceLang, CancellationToken ct, UsageStats? usage,
        IProgress<TranslationProgress>? progress, int cumStart, int cumTotal)
    {
        var itemsJson = BuildItemsJson(batch);
        var systemPrompt = BuildSystemPrompt(targetLang, EffectiveSource(sourceLang, batch.Select(b => b.Text)), batch.Count);
        var messages = new List<ModelMessage>
        {
            new("system", systemPrompt),
            new("user", itemsJson),
        };

        var options = new ModelRequestOptions
        {
            Temperature = (decimal)advanced.Temperature,
            JsonMode = true,
            MaxTokens = EstimateMaxTokens(batch.Count),
            UsageCallback = usage == null ? null : u => usage.Merge(u),
        };

        // 流式拉取响应（空则回退一次非流式请求）；边收边上报，让界面实时滚动
        var content = await StreamTextAsync(client, messages, options, ct, progress, cumStart, cumTotal).ConfigureAwait(false);
        var parsed = ParseTranslations(content);

        var result = new List<(int Index, string Text)>();
        // 自校验：缺失 / 回显原文 / 丢占位符 都判为"未真正翻译"，收集诊断待模型自校正
        var bad = new List<(int Index, string Text, string Reason, string LastOutput)>();
        foreach (var (idx, text) in batch)
        {
            if (parsed == null || !parsed.TryGetValue(idx, out var translated) || string.IsNullOrWhiteSpace(translated))
            {
                bad.Add((idx, text, "missing", "(no output)"));
                continue;
            }
            var trimmed = translated.Trim();
            if (string.Equals(trimmed, text, StringComparison.Ordinal))
            {
                bad.Add((idx, text, "echo", trimmed));
                continue;
            }
            if (!ContainsAllPlaceholders(trimmed, CollectPlaceholders(text)))
            {
                bad.Add((idx, text, "placeholder", trimmed));
                continue;
            }
            result.Add((idx, trimmed));
        }

        // 自校正闭环：把"错在哪"回给模型，让其修正这些条目（而非机械盲重发同一条）
        if (bad.Count > 0)
        {
            progress?.Report(new TranslationProgress(cumStart, cumTotal, $"自校正 {bad.Count} 条…"));
            var fixedTx = new List<(int Index, string Text)>();
            try
            {
                fixedTx = (await SelfCorrectBatchAsync(client, advanced, bad, targetLang, sourceLang, ct, usage, progress, cumStart, cumTotal).ConfigureAwait(false)).ToList();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 宿主取消 → 立即停止
            }
            catch
            {
                // 自校正请求失败：仅跳过坏项，已翻译成功的保留，坏项留待下次同步/人环补
            }
            result.AddRange(fixedTx);
        }
        return result;
    }

    /// <summary>流式拉取整段响应文本；流式拿不到（后端不支持/连接即断）回退一次非流式，保证兼容。</summary>
    private static async Task<string> StreamTextAsync(
        IModelClient client, IReadOnlyList<ModelMessage> messages, ModelRequestOptions options,
        CancellationToken ct, IProgress<TranslationProgress>? progress, int cumStart, int cumTotal)
    {
        var sb = new StringBuilder();
        var n = 0;
        await foreach (var chunk in client.CompleteStreamAsync(messages, options, ct).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(chunk.Text)) continue;
            sb.Append(chunk.Text);
            n++;
            if (n % 20 == 0)
                progress?.Report(new TranslationProgress(cumStart, cumTotal, $"正在接收响应…（{sb.Length} 字）"));
        }
        if (sb.Length > 0)
        {
            progress?.Report(new TranslationProgress(cumStart, cumTotal, $"正在接收响应…（{sb.Length} 字）"));
            return sb.ToString();
        }
        var resp = await client.CompleteAsync(messages, options, ct).ConfigureAwait(false);
        return resp?.Content ?? "";
    }

    /// <summary>自校正一批"翻错/缺失/丢占位符"的条目：把每条诊断回给模型，让它重新翻译成目标语言。</summary>
    private static async Task<List<(int Index, string Text)>> SelfCorrectBatchAsync(
        IModelClient client, ModelAdvancedConfig advanced, List<(int Index, string Text, string Reason, string LastOutput)> bad,
        string targetLang, string sourceLang, CancellationToken ct, UsageStats? usage,
        IProgress<TranslationProgress>? progress, int cumStart, int cumTotal)
    {
        var messages = new List<ModelMessage>
        {
            new("system", BuildSystemPrompt(targetLang, EffectiveSource(sourceLang, bad.Select(b => b.Text)), bad.Count)),
            new("user", BuildCorrectionJson(bad, ToLanguageName(targetLang))),
        };
        var options = new ModelRequestOptions
        {
            Temperature = (decimal)advanced.Temperature,
            JsonMode = true,
            MaxTokens = EstimateMaxTokens(bad.Count),
            UsageCallback = usage == null ? null : u => usage.Merge(u),
        };
        var content = await StreamTextAsync(client, messages, options, ct, progress, cumStart, cumTotal).ConfigureAwait(false);
        var parsed = ParseTranslations(content);

        var ok = new List<(int Index, string Text)>();
        if (parsed == null) return ok;
        foreach (var (idx, text, _, _) in bad)
        {
            if (!parsed.TryGetValue(idx, out var tr) || string.IsNullOrWhiteSpace(tr)) continue;
            var t = tr.Trim();
            if (string.Equals(t, text, StringComparison.Ordinal)) continue;          // 仍回显 → 留待下次/人环
            if (!ContainsAllPlaceholders(t, CollectPlaceholders(text))) continue;    // 仍丢占位符 → 留待下次/人环
            ok.Add((idx, t));
        }
        return ok;
    }

    /// <summary>构造自校正指令：列出待修正条目与上次的错误诊断。</summary>
    private static string BuildCorrectionJson(List<(int Index, string Text, string Reason, string LastOutput)> bad, string targetName)
    {
        var sb = new StringBuilder();
        sb.Append("Some items from your previous batch were NOT translated correctly. Re-translate ONLY these into ")
          .Append(targetName)
          .Append(". Keep {..} placeholders verbatim. Output a JSON object with key \"translations\": [{\"id\":number,\"translated\":string}] including every id below exactly once.\nItems to fix:\n");
        foreach (var (idx, text, reason, last) in bad)
        {
            var problem = reason switch
            {
                "echo" => "you returned the SOURCE text unchanged - it must be an actual " + targetName + " translation",
                "placeholder" => "you dropped or changed a {..} placeholder - keep them verbatim",
                _ => "no translation was produced for this id",
            };
            sb.Append("id=").Append(idx)
              .Append("; source=").Append(JsonSerializer.Serialize(text, s_promptStringInfo))
              .Append("; previous output=").Append(JsonSerializer.Serialize(last, s_promptStringInfo))
              .Append("; problem: ").Append(problem).Append('\n');
        }
        return sb.ToString();
    }

    // ===== JSON 构建/解析 =====

    private static string BuildItemsJson(List<(int Index, string Text)> batch)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < batch.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(batch[i].Index)
              .Append(",\"text\":").Append(JsonSerializer.Serialize(batch[i].Text, s_promptStringInfo))
              .Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>解析 "{"translations":[{"id":0,"translated":"..."}]}"。宽容处理：剥 markdown 围栏、容忍截断，尽量提取已成功的条目。</summary>
    private static Dictionary<int, string>? ParseTranslations(string content)
    {
        content = (content ?? "").Trim();
        if (content.Length == 0) return null;

        // 剥 model 可能包裹的 ```json ... ``` 围栏
        var fence = content.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var firstBrace = content.IndexOf('{', fence);
            var fenceEnd = content.LastIndexOf("```", StringComparison.Ordinal);
            var lastBrace = fenceEnd > fence ? content.LastIndexOf('}', fenceEnd) : -1;
            if (firstBrace >= 0)
                content = lastBrace > firstBrace
                    ? content.Substring(firstBrace, lastBrace - firstBrace + 1)
                    : content.Substring(firstBrace);
        }

        var parsed = TryParse(content);
        if (parsed != null) return parsed;

        // 可能被截断：取首个 { 到最后一个 } 的片段再试
        var lo = content.IndexOf('{');
        var hi = content.LastIndexOf('}');
        if (lo >= 0 && hi > lo)
            return TryParse(content.Substring(lo, hi - lo + 1));
        return null;
    }

    private static Dictionary<int, string>? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var inner = JsonDocument.Parse(json);
            if (!inner.RootElement.TryGetProperty("translations", out var translations))
                return null;
            var dict = new Dictionary<int, string>();
            foreach (var item in translations.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idEl) || !item.TryGetProperty("translated", out var trEl))
                    continue;
                if (idEl.TryGetInt32(out var id) && trEl.ValueKind == JsonValueKind.String)
                    dict[id] = trEl.GetString() ?? "";
            }
            return dict.Count == 0 ? null : dict;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ===== 占位符 =====

    /// <summary>推断有效源语言：显式给定则用之；否则若本批文本含 CJK 则判定为简体中文，明确告知模型避免误判而回显。</summary>
    private static string EffectiveSource(string sourceLang, IEnumerable<string> texts)
    {
        if (!string.IsNullOrWhiteSpace(sourceLang))
            return sourceLang;
        return texts.Any(ContainsCjk) ? "Chinese (simplified)" : "";
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if (ch is >= '\u4E00' and <= '\u9FFF'
                or >= '\u3400' and <= '\u4DBF'
                or >= '\uF900' and <= '\uFAFF')
                return true;
        }
        return false;
    }

    private static void CollectPlaceholders(string text, HashSet<string> set)
    {
        if (string.IsNullOrEmpty(text)) return;
        var i = 0;
        while (i < text.Length)
        {
            var open = text.IndexOf('{', i);
            if (open < 0) break;
            var close = text.IndexOf('}', open + 1);
            if (close < 0) break;
            set.Add(text.Substring(open, close - open + 1));
            i = close + 1;
        }
    }

    private static HashSet<string> CollectPlaceholders(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        CollectPlaceholders(text, set);
        return set;
    }

    private static bool ContainsAllPlaceholders(string translated, HashSet<string> placeholders)
    {
        foreach (var ph in placeholders)
            if (!translated.Contains(ph, StringComparison.Ordinal))
                return false;
        return true;
    }

    /// <summary>系统提示（目标语言 = locale code 映射为自然语言；源语言可空=自动检测）。</summary>
    private static string BuildSystemPrompt(string targetLang, string sourceLang, int itemCount)
    {
        var targetName = ToLanguageName(targetLang);
        var sourceClause = string.IsNullOrWhiteSpace(sourceLang)
            ? "The source text may be in Chinese, English or any other language — auto-detect it per item."
            : $"The source language is {sourceLang}.";
        return
            "You are a professional game dialogue translator. " + sourceClause + " " +
            $"Translate the JSON array items into {targetName}. Output the translation directly in {targetName} " +
            "(e.g. if the target is Japanese, produce Japanese text; if English, produce English; if French, produce French). " +
            "Output a JSON object with key \"translations\": an array of {\"id\": number, \"translated\": string}. " +
            $"There are {itemCount} items; every id must appear exactly once. " +
            "Rules: 1) Return ONLY valid JSON, no explanations, no markdown fences. " +
            "2) Preserve {{...}} placeholders (e.g. {{player.name}}, {{b}}, {{/b}}, {{color=#FFD700}}, {{p}}) verbatim in the same positions. " +
            "3) Keep speaker names and proper nouns in original language when appropriate. " +
            "4) Keep numbers, punctuation and line breaks.";
    }

    /// <summary>locale code → 自然语言名（目录名仍用 locale code，prompt 内映射）。</summary>
    private static string ToLanguageName(string localeCode)
    {
        if (string.IsNullOrWhiteSpace(localeCode)) return localeCode;
        var code = localeCode.Split('-')[0].ToUpperInvariant();
        return code switch
        {
            "ZH" => "Chinese (Simplified)",
            "EN" => "English",
            "JA" => "Japanese",
            "KO" => "Korean",
            "FR" => "French",
            "DE" => "German",
            "ES" => "Spanish",
            "RU" => "Russian",
            "PT" => "Portuguese",
            "IT" => "Italian",
            _ => localeCode,
        };
    }
}