using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// AI 智能翻译器——调用 OpenAI 兼容 LLM API 批量翻译。
/// <para>协议：POST {BaseUrl}/chat/completions，标准 chat 接口 + JSON 模式（response_format=json_object）。</para>
/// <para>可接任意 OpenAI 兼容端点：DeepSeek（默认）/OpenAI/Qwen/Moonshot/本地 Ollama 等。</para>
/// <para>批量：一次请求翻译 ≤BatchSize 条，system prompt 摊薄成本（官方建议 50 条/批 ≈ 30× 成本摊薄）；
/// 占位符（{var}/{b}{color=}）提取后程序化校验，缺失自动重试。</para>
/// </summary>
public sealed class AiTranslator : ITranslator
{
    private readonly ModelConfig _config;
    private readonly IHttpClientFactory? _httpClientFactory;

    /// <summary>创建 AI 翻译器（注入 IHttpClientFactory 复用连接池；无则内部创建）</summary>
    public AiTranslator(ModelConfig config, IHttpClientFactory? httpClientFactory = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public async Task<string?> TranslateAsync(string text, string targetLang, string sourceLang = "", CancellationToken ct = default)
    {
        var batch = await TranslateBatchAsync([text], targetLang, sourceLang, ct).ConfigureAwait(false);
        return batch.Count > 0 ? batch[0] : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts, string targetLang, string sourceLang = "", CancellationToken ct = default)
    {
        var results = new string?[texts.Count];
        if (texts.Count == 0) return results;

        if (string.IsNullOrWhiteSpace(_config.ApiKey) && !IsLocal(_config.BaseUrl))
            return results; // 无 Key 且非本地端点 → 全失败

        var url = _config.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
        if (_httpClientFactory == null)
            client.Timeout = TimeSpan.FromSeconds(_config.Advanced.TimeoutSeconds);

        // 分批
        var batchSize = Math.Max(1, _config.Advanced.BatchSize);
        for (var start = 0; start < texts.Count; start += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = new List<(int Index, string Text)>();
            var end = Math.Min(start + batchSize, texts.Count);
            for (var i = start; i < end; i++)
                batch.Add((i, texts[i]));

            var translated = await TranslateBatchOnceAsync(batch, targetLang, sourceLang, url, client, ct).ConfigureAwait(false);
            foreach (var (idx, text) in translated)
                results[idx] = text;
        }

        return results;
    }

    private async Task<List<(int Index, string Text)>> TranslateBatchOnceAsync(
        List<(int Index, string Text)> batch, string targetLang, string sourceLang,
        string url, HttpClient client, CancellationToken ct)
    {
        // 请求体：system（翻译规则）+ user（JSON 数组 [{id, text}]）
        var itemsJson = BuildItemsJson(batch);
        var body = "{\"model\":" + JsonSerializer.Serialize(_config.ModelId, SdkJsonContext.Default.String)
            + ",\"temperature\":" + _config.Advanced.Temperature.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)
            + ",\"response_format\":{\"type\":\"json_object\"}"
            + ",\"messages\":["
            + "{\"role\":\"system\",\"content\":" + JsonSerializer.Serialize(BuildSystemPrompt(targetLang, sourceLang, batch.Count), SdkJsonContext.Default.String) + "},"
            + "{\"role\":\"user\",\"content\":" + JsonSerializer.Serialize(itemsJson, SdkJsonContext.Default.String) + "}"
            + "]}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);

        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return []; // 整批失败

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = ParseBatchResponse(json, batch.Count);
        if (parsed == null)
            return [];

        // 程序化占位符校验：每条校验自己的占位符（缺失 → 重试一次；仍缺 → 丢弃该条）
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
            var ownPlaceholders = new HashSet<string>(StringComparer.Ordinal);
            CollectPlaceholders(text, ownPlaceholders);
            if (ContainsAllPlaceholders(trimmed, ownPlaceholders))
                result.Add((idx, trimmed));
            else
                needsRetry.Add((idx, text)); // 自己的占位符丢失 → 重试
        }

        if (needsRetry.Count > 0)
        {
            // 重试一次（限单条，降低再次整批失败概率）
            foreach (var (idx, text) in needsRetry)
            {
                ct.ThrowIfCancellationRequested();
                var single = await TranslateSingleWithRetryAsync(text, targetLang, sourceLang, url, client, ct).ConfigureAwait(false);
                if (single != null)
                    result.Add((idx, single));
            }
        }

        return result;
    }

    /// <summary>重试单条翻译（占位符校验，最多 2 次）</summary>
    private async Task<string?> TranslateSingleWithRetryAsync(
        string text, string targetLang, string sourceLang, string url, HttpClient client, CancellationToken ct)
    {
        var placeholders = new HashSet<string>(StringComparer.Ordinal);
        CollectPlaceholders(text, placeholders);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var itemsJson = BuildItemsJson([(0, text)]);
            var body = "{\"model\":" + JsonSerializer.Serialize(_config.ModelId, SdkJsonContext.Default.String)
                + ",\"temperature\":" + _config.Advanced.Temperature.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture)
                + ",\"response_format\":{\"type\":\"json_object\"}"
                + ",\"messages\":["
                + "{\"role\":\"system\",\"content\":" + JsonSerializer.Serialize(BuildSystemPrompt(targetLang, sourceLang, 1), SdkJsonContext.Default.String) + "},"
                + "{\"role\":\"user\",\"content\":" + JsonSerializer.Serialize(itemsJson, SdkJsonContext.Default.String) + "}"
                + "]}";

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrWhiteSpace(_config.ApiKey))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);

            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = ParseBatchResponse(json, 1);
            if (parsed == null || !parsed.TryGetValue(0, out var translated) || string.IsNullOrWhiteSpace(translated))
                return null;

            var trimmed = translated.Trim();
            if (ContainsAllPlaceholders(trimmed, placeholders))
                return trimmed;
            // 占位符仍缺 → 下次重试
        }
        return null;
    }

    // ===== JSON 构建与解析（AOT 安全，手动拼/解析） =====

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

    /// <summary>解析批量响应：{ "translations": [{"id":0,"translated":"..."}] } → id→译文</summary>
    private static Dictionary<int, string>? ParseBatchResponse(string json, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;
            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                return null;

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
        catch (Exception)
        {
            return null; // JSON 解析失败
        }
    }

    // ===== 占位符工具 =====

    /// <summary>提取文本中所有 {…} 占位符（含富文本标记 {b} {/b} {color=#FFF} 与变量 {player.name}）</summary>
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
            var placeholder = text.Substring(open, close - open + 1);
            set.Add(placeholder);
            i = close + 1;
        }
    }

    private static bool ContainsAllPlaceholders(string translated, HashSet<string> placeholders)
    {
        foreach (var ph in placeholders)
        {
            if (!translated.Contains(ph, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 构造系统提示词。
    /// <para>关键设计：targetLang 是引擎 i18n 标准的 locale code（en-US/ja-JP/ko-KR…），仅用作语言根目录名；
    /// prompt 里把它映射成自然语言（如 ja-JP→Japanese）告诉模型输出何种语言——既遵循标准、又不写死 en-US。
    /// 源语言由调用方提供（自然语言提示，留空=让模型自动检测）。</para>
    /// </summary>
    private static string BuildSystemPrompt(string targetLang, string sourceLang, int itemCount)
    {
        var targetName = ToLanguageName(targetLang);
        var sourceClause = string.IsNullOrWhiteSpace(sourceLang)
            ? "The source text may be in Chinese, English or any other language — auto-detect it per item."
            : $"The source language is {sourceLang}.";

        return
            "You are a professional game dialogue translator. " +
            sourceClause + " " +
            $"Translate the JSON array items into {targetName}. Output the translation directly in {targetName} " +
            "(e.g. if the target is Japanese, produce Japanese text; if English, produce English; if French, produce French). " +
            "Output a JSON object with key \"translations\": an array of {\"id\": number, \"translated\": string}. " +
            $"There are {itemCount} items; every id must appear exactly once. " +
            "Rules: 1) Return ONLY valid JSON, no explanations, no markdown fences. " +
            "2) Preserve {{...}} placeholders (e.g. {{player.name}}, {{b}}, {{/b}}, {{color=#FFD700}}, {{p}}) verbatim in the same positions. " +
            "3) Keep speaker names and proper nouns in original language when appropriate. " +
            "4) Keep numbers, punctuation and line breaks.";
    }

    /// <summary>将引擎 locale code 映射为自然语言名喂给翻译模型（目录名仍用 locale code，遵守标准）。未知 code 原样返回。</summary>
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
            "ZH-TW" => "Chinese (Traditional)",
            _ => localeCode,
        };
    }

    private static bool IsLocal(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;
        var lower = baseUrl.ToLowerInvariant();
        return lower.Contains("localhost") || lower.Contains("127.0.0.1");
    }
}
