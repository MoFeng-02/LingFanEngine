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
/// Anthropic（Claude）翻译器——调用 Messages API 批量翻译。
/// <para>协议：POST {BaseUrl}/v1/messages，强制头 <c>x-api-key</c> + <c>anthropic-version: 2023-06-01</c>；
/// <c>max_tokens</c> 必填。系统提示与占位符校验逻辑与 <see cref="AiTranslator"/> 一致。</para>
/// <para>接入 Anthropic 官方或任意兼容 Messages 协议的端点（如本地代理）。</para>
/// </summary>
public sealed class AnthropicTranslator : ITranslator
{
    private readonly ModelConfig _config;
    private readonly IHttpClientFactory? _httpClientFactory;

    /// <summary>创建 Anthropic 翻译器</summary>
    public AnthropicTranslator(ModelConfig config, IHttpClientFactory? httpClientFactory = null)
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
            return results;

        var url = _config.BaseUrl.TrimEnd('/') + "/v1/messages";
        using var client = _httpClientFactory?.CreateClient() ?? new HttpClient();
        if (_httpClientFactory == null)
            client.Timeout = TimeSpan.FromSeconds(_config.Advanced.TimeoutSeconds);

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
        var itemsJson = BuildItemsJson(batch);
        var systemPrompt = BuildSystemPrompt(targetLang, sourceLang, batch.Count);
        // Anthropic 要求 max_tokens（必填）；按批量大小估算输出上限
        var maxTokens = Math.Min(16384, Math.Max(1024, batch.Count * 512));
        var body = "{\"model\":" + JsonSerializer.Serialize(_config.ModelId, SdkJsonContext.Default.String)
            + ",\"max_tokens\":" + maxTokens
            + ",\"system\":" + JsonSerializer.Serialize(systemPrompt, SdkJsonContext.Default.String)
            + ",\"messages\":["
            + "{\"role\":\"user\",\"content\":" + JsonSerializer.Serialize(itemsJson, SdkJsonContext.Default.String) + "}"
            + "]}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("x-api-key", _config.ApiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return [];

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = ParseBatchResponse(json, batch.Count);
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
            var ownPlaceholders = new HashSet<string>(StringComparer.Ordinal);
            CollectPlaceholders(text, ownPlaceholders);
            if (ContainsAllPlaceholders(trimmed, ownPlaceholders))
                result.Add((idx, trimmed));
            else
                needsRetry.Add((idx, text));
        }

        if (needsRetry.Count > 0)
        {
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

    private async Task<string?> TranslateSingleWithRetryAsync(
        string text, string targetLang, string sourceLang, string url, HttpClient client, CancellationToken ct)
    {
        var placeholders = new HashSet<string>(StringComparer.Ordinal);
        CollectPlaceholders(text, placeholders);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var itemsJson = BuildItemsJson([(0, text)]);
            var systemPrompt = BuildSystemPrompt(targetLang, sourceLang, 1);
            var maxTokens = Math.Min(16384, Math.Max(1024, 512));
            var body = "{\"model\":" + JsonSerializer.Serialize(_config.ModelId, SdkJsonContext.Default.String)
                + ",\"max_tokens\":" + maxTokens
                + ",\"system\":" + JsonSerializer.Serialize(systemPrompt, SdkJsonContext.Default.String)
                + ",\"messages\":["
                + "{\"role\":\"user\",\"content\":" + JsonSerializer.Serialize(itemsJson, SdkJsonContext.Default.String) + "}"
                + "]}";

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("x-api-key", _config.ApiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

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

    /// <summary>解析 Anthropic 响应：content[0].text 为 JSON 字符串 → { "translations": [...] }</summary>
    private static Dictionary<int, string>? ParseBatchResponse(string json, int expectedCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("content", out var content) || content.GetArrayLength() == 0)
                return null;
            var text = content[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            using var inner = JsonDocument.Parse(text);
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
            return null;
        }
    }

    // ===== 占位符工具 =====

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

    private static bool ContainsAllPlaceholders(string translated, HashSet<string> placeholders)
    {
        foreach (var ph in placeholders)
        {
            if (!translated.Contains(ph, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

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
