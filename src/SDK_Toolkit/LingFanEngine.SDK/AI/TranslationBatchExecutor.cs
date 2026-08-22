using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.AI;

/// <summary>
/// 共享的批量翻译执行器——把 "多文本 → 批量请求 → 解析 → 占位符校验 → 单条重试" 收敛到一处。
/// <para>OpenAI 兼容 / Anthropic 客户端都经此执行，杜绝此前 <c>AiTranslator/AnthropicTranslator</c> 95% 重复。</para>
/// </summary>
public static class TranslationBatchExecutor
{
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
        progress?.Report(new TranslationProgress(0, texts.Count, ""));
        for (var start = 0; start < texts.Count; start += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var end = Math.Min(start + batchSize, texts.Count);
            var batch = new List<(int Index, string Text)>();
            for (var i = start; i < end; i++)
                batch.Add((i, texts[i]));

            var translated = await BatchOnceAsync(client, advanced, batch, targetLang, sourceLang, ct, usage).ConfigureAwait(false);
            foreach (var (idx, text) in translated)
                results[idx] = text;

            progress?.Report(new TranslationProgress(end, texts.Count, texts[end - 1]));
        }
        return results;
    }

    private static async Task<List<(int Index, string Text)>> BatchOnceAsync(
        IModelClient client, ModelAdvancedConfig advanced, List<(int Index, string Text)> batch,
        string targetLang, string sourceLang, CancellationToken ct, UsageStats? usage)
    {
        var itemsJson = BuildItemsJson(batch);
        var systemPrompt = BuildSystemPrompt(targetLang, sourceLang, batch.Count);
        var messages = new List<ModelMessage>
        {
            new("system", systemPrompt),
            new("user", itemsJson),
        };

        var resp = await client.CompleteAsync(messages, new ModelRequestOptions
        {
            Temperature = (decimal)advanced.Temperature,
            JsonMode = true,
            UsageCallback = usage == null ? null : u => usage.Merge(u),
        }, ct).ConfigureAwait(false);
        var parsed = resp?.Content == null ? null : ParseTranslations(resp.Content);
        if (parsed == null)
            return [];

        var result = new List<(int Index, string Text)>();
        var needsRetry = new List<(int Index, string Text)>();
        foreach (var (idx, text) in batch)
        {
            if (!parsed.TryGetValue(idx, out var translated) || string.IsNullOrWhiteSpace(translated))
            {
                needsRetry.Add((idx, text));
                continue;
            }
            var trimmed = translated.Trim();
            if (ContainsAllPlaceholders(trimmed, CollectPlaceholders(text)))
                result.Add((idx, trimmed));
            else
                needsRetry.Add((idx, text)); // 自己的占位符丢失 → 重试
        }

        if (needsRetry.Count > 0)
        {
            foreach (var (idx, text) in needsRetry)
            {
                ct.ThrowIfCancellationRequested();
                var single = await TranslateSingleWithRetryAsync(client, advanced, text, targetLang, sourceLang, ct, usage).ConfigureAwait(false);
                if (single != null)
                    result.Add((idx, single));
            }
        }
        return result;
    }

    /// <summary>单条重试翻译（占位符校验，最多 2 次）。</summary>
    private static async Task<string?> TranslateSingleWithRetryAsync(
        IModelClient client, ModelAdvancedConfig advanced, string text,
        string targetLang, string sourceLang, CancellationToken ct, UsageStats? usage)
    {
        var placeholders = CollectPlaceholders(text);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var messages = new List<ModelMessage>
            {
                new("system", BuildSystemPrompt(targetLang, sourceLang, 1)),
                new("user", BuildItemsJson([(0, text)])),
            };
            var resp = await client.CompleteAsync(messages, new ModelRequestOptions
            {
                Temperature = (decimal)advanced.Temperature,
                JsonMode = true,
                UsageCallback = usage == null ? null : u => usage.Merge(u),
            }, ct).ConfigureAwait(false);
            var parsed = resp?.Content == null ? null : ParseTranslations(resp.Content);
            if (parsed == null || !parsed.TryGetValue(0, out var translated) || string.IsNullOrWhiteSpace(translated))
                return null;
            var trimmed = translated.Trim();
            if (ContainsAllPlaceholders(trimmed, placeholders))
                return trimmed;
        }
        return null;
    }

    // ===== JSON 构建/解析 =====

    private static string BuildItemsJson(List<(int Index, string Text)> batch)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < batch.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(batch[i].Index)
              .Append(",\"text\":").Append(JsonSerializer.Serialize(batch[i].Text, SdkJsonContext.Default.String))
              .Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>解析 "{"translations":[{"id":0,"translated":"..."}]}"。</summary>
    private static Dictionary<int, string>? ParseTranslations(string content)
    {
        try
        {
            using var inner = JsonDocument.Parse(content);
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
            return dict;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ===== 占位符 =====

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