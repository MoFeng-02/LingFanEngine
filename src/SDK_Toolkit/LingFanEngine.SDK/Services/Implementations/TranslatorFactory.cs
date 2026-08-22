using System;
using LingFanEngine.SDK.AI;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 翻译器工厂——按模型配置经 <see cref="IModelClientFactory"/> 创建客户端并包装为 <see cref="LlmTranslator"/>。
/// <para>配置无效（缺模型 ID，或缺 API Key 且非本地端点）返回 null，调用方提示补全。</para>
/// </summary>
public sealed class TranslatorFactory : ITranslatorFactory
{
    private readonly IModelClientFactory _modelClientFactory;

    /// <summary>创建翻译器工厂（客户端由模型工厂统一经 IHttpClientFactory 创建）。</summary>
    public TranslatorFactory(IModelClientFactory modelClientFactory)
    {
        _modelClientFactory = modelClientFactory ?? throw new ArgumentNullException(nameof(modelClientFactory));
    }

    /// <inheritdoc/>
    public ITranslator? Create(ModelConfig model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));
        if (string.IsNullOrWhiteSpace(model.ModelId)) return null;
        if (string.IsNullOrWhiteSpace(model.ApiKey) && !ModelClientFactory.IsLocal(model.BaseUrl)) return null;

        var client = _modelClientFactory.Create(model);
        if (client == null) return null;
        return new LlmTranslator(client, model.Advanced);
    }

    /// <summary>本地端点判断（供 TranslationViewModel 提示复用）。</summary>
    internal static bool IsLocal(string baseUrl) => ModelClientFactory.IsLocal(baseUrl);
}