using System;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 翻译器工厂——按 <see cref="ModelConfig.Provider"/> 分发 OpenAI / Anthropic 实现。
/// <para>复用注入的 <see cref="System.Net.Http.IHttpClientFactory"/> 连接池。</para>
/// </summary>
public sealed class TranslatorFactory : ITranslatorFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>创建翻译器工厂</summary>
    public TranslatorFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <inheritdoc/>
    public ITranslator? Create(ModelConfig model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (string.IsNullOrWhiteSpace(model.ModelId)) return null;
        if (string.IsNullOrWhiteSpace(model.ApiKey) && !IsLocal(model.BaseUrl)) return null;

        return model.Provider switch
        {
            ModelProvider.Anthropic => new AnthropicTranslator(model, _httpClientFactory),
            _ => new AiTranslator(model, _httpClientFactory),
        };
    }

    /// <summary>本地端点判断（localhost / 127.0.0.1 免 API Key）——TranslationViewModel 提示复用</summary>
    internal static bool IsLocal(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;
        var lower = baseUrl.ToLowerInvariant();
        return lower.Contains("localhost") || lower.Contains("127.0.0.1");
    }
}
