using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 专业翻译 API 翻译器——调用 DeepL 风格翻译端点。
/// <para>协议：POST {Endpoint}，form 编码 text + target_lang，Authorization: DeepL-Auth-Key。</para>
/// <para>富文本：DeepL 支持 tag_handling=html 保留 XML/HTML 标记（对 DSL 富文本 {b}{color=} 友好，见调研确认）。</para>
/// </summary>
public sealed class ApiTranslator : ITranslator
{
    private readonly ApiTranslatorConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>创建翻译 API 翻译器（统一经 IHttpClientFactory，复用连接池）。</summary>
    public ApiTranslator(ApiTranslatorConfig config, IHttpClientFactory httpClientFactory)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <inheritdoc/>
    public Task<string?> TranslateAsync(string text, string targetLang, string sourceLang = "", CancellationToken ct = default)
        => TranslateBatchAsync([text], targetLang, sourceLang, ct).ContinueWith(
            t => t.Result.Count > 0 ? t.Result[0] : null, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string?>> TranslateBatchAsync(
        IReadOnlyList<string> texts, string targetLang, string sourceLang = "", CancellationToken ct = default)
    {
        var results = new string?[texts.Count];
        if (texts.Count == 0) return results;
        if (string.IsNullOrWhiteSpace(_config.Endpoint) || string.IsNullOrWhiteSpace(_config.ApiKey))
            return results;

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        // DeepL 支持一次请求多条（重复 text 参数）；分批限制单次大小防 URL 过大
        const int batchSize = 50;
        for (var start = 0; start < texts.Count; start += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var end = Math.Min(start + batchSize, texts.Count);
            var batch = new List<string>();
            for (var i = start; i < end; i++) batch.Add(texts[i]);

            var translated = await TranslateBatchOnceAsync(batch, targetLang, sourceLang, client, ct).ConfigureAwait(false);
            for (var i = 0; i < translated.Count && start + i < texts.Count; i++)
                results[start + i] = translated[i];
        }

        return results;
    }

    private async Task<List<string?>> TranslateBatchOnceAsync(
        List<string> batch, string targetLang, string sourceLang, HttpClient client, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["target_lang"] = string.IsNullOrEmpty(_config.TargetLangCode) ? NormalizeLang(targetLang) : _config.TargetLangCode,
        };
        // DeepL tag_handling=html：保留富文本标记（{b}{color=} 等 DSL 内联标记）
        form["tag_handling"] = "html";

        // 源语言：留空让 DeepL 自动检测；非空则标准化为 2 字母代码（如 ZH/EN/JA）
        var sourcePart = "";
        if (!string.IsNullOrWhiteSpace(sourceLang))
        {
            var srcCode = NormalizeLang(sourceLang);
            // DeepL source_lang 只需 2 字母（EN 而非 EN-US）
            srcCode = srcCode.Split('-')[0];
            form["source_lang"] = srcCode;
            sourcePart = "&source_lang=" + Uri.EscapeDataString(srcCode);
        }

        // DeepL 批量 = 重复 text 参数（text=a&text=b）；FormUrlEncodedContent 不支持重复 key，手拼 body
        var body = "target_lang=" + Uri.EscapeDataString(form["target_lang"])
            + "&tag_handling=html"
            + sourcePart;
        foreach (var t in batch)
            body += "&text=" + Uri.EscapeDataString(t);

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");

        using var req = new HttpRequestMessage(HttpMethod.Post, _config.Endpoint)
        {
            Content = content,
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {_config.ApiKey}");

        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return batch.Select(_ => (string?)null).ToList();

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("translations", out var translations))
                return batch.Select(_ => (string?)null).ToList();

            var result = new List<string?>();
            foreach (var item in translations.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                    result.Add(textEl.GetString()?.Trim());
                else
                    result.Add(null);
            }
            return result;
        }
        catch (Exception)
        {
            return batch.Select(_ => (string?)null).ToList();
        }
    }

    private static string NormalizeLang(string lang)
    {
        // zh-CN → ZH，en-US → EN-US，ja-JP → JA
        var code = lang.Split('-')[0].ToUpperInvariant();
        return code switch
        {
            "ZH" => "ZH",
            "EN" => "EN-US",
            "JA" => "JA",
            "KO" => "KO",
            "FR" => "FR",
            "DE" => "DE",
            "RU" => "RU",
            "ES" => "ES",
            _ => code,
        };
    }
}
