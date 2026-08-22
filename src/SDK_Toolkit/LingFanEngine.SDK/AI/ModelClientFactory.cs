using System;
using System.Net.Http;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.AI;

/// <summary>按模型配置创建对应客户端（OpenAI / Anthropic 分发）。</summary>
public interface IModelClientFactory
{
    /// <summary>创建客户端；配置无效（缺模型 ID）时返回 null。</summary>
    IModelClient? Create(ModelConfig model);
}

/// <summary>客户端工厂——按 <see cref="ModelProvider"/> 分发，统一注入 <see cref="IHttpClientFactory"/>。</summary>
public sealed class ModelClientFactory : IModelClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModelClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <inheritdoc/>
    public IModelClient? Create(ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.ModelId))
            return null;
        return model.Provider switch
        {
            ModelProvider.Anthropic => new AnthropicClient(model, _httpClientFactory),
            _ => new OpenAiCompatibleClient(model, _httpClientFactory),
        };
    }

    /// <summary>本地端点判断（localhost / 127.0.0.1）——供外部提示复用。</summary>
    public static bool IsLocal(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;
        var lower = baseUrl.ToLowerInvariant();
        return lower.Contains("localhost") || lower.Contains("127.0.0.1");
    }
}