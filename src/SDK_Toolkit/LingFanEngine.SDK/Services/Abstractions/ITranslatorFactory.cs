using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>按模型配置创建对应翻译器（OpenAI / Anthropic 分发）</summary>
public interface ITranslatorFactory
{
    /// <summary>
    /// 从模型配置创建翻译器。
    /// <para>配置无效（缺 API Key 且非本地端点、或缺模型 ID）时返回 null，调用方应提示用户补全。</para>
    /// </summary>
    ITranslator? Create(ModelConfig model);
}
