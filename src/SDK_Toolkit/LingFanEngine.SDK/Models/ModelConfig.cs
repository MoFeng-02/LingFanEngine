using System;
using System.Text.Json.Serialization;

namespace LingFanEngine.SDK.Models;

/// <summary>AI 翻译模型提供方（决定请求协议）</summary>
public enum ModelProvider
{
    /// <summary>OpenAI Chat Completions 兼容接口（含 DeepSeek/Qwen/Moonshot/本地 Ollama 等）</summary>
    OpenAi,

    /// <summary>Anthropic Messages 接口（Claude 系列）</summary>
    Anthropic,
}

/// <summary>模型高级配置（对话框「高级配置」折叠面板内容）</summary>
public sealed class ModelAdvancedConfig
{
    /// <summary>温度（0-1，批量翻译建议 0.2-0.3 保持一致性）</summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>每批翻译的条目数（越小越稳定、越少回显/截断；越大越省请求但易被模型偷懒）。可在模型高级配置中显式调整。</summary>
    public int BatchSize { get; set; } = 4;

    /// <summary>HTTP 超时（秒）</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// 可复用的翻译模型配置（对应「自定义模型」对话框）。
/// <para>持久化到 <c>%LOCALAPPDATA%/LingFanEngine.SDK/models.json</c>，AOT 安全（<see cref="Utils.SdkJsonContext"/> 源生成）。</para>
/// </summary>
public sealed class ModelConfig
{
    /// <summary>唯一 ID（自动生成）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>提供方（决定请求协议：OpenAI Chat Completions / Anthropic Messages）</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ModelProvider>))]
    public ModelProvider Provider { get; set; } = ModelProvider.OpenAi;

    /// <summary>模型展示名称（选填；未填时列表/下拉用 <see cref="ModelId"/> 显示）</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>API Key（密码框输入）</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>API Base URL（如 https://api.openai.com/v1；Anthropic 默认 https://api.anthropic.com）</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>模型 ID（必填，如 gpt-4o-mini / claude-3-5-sonnet-20241022）</summary>
    public string ModelId { get; set; } = "";

    /// <summary>高级配置</summary>
    public ModelAdvancedConfig Advanced { get; set; } = new();

    /// <summary>列表中显示的名称：展示名称优先，否则模型 ID</summary>
    public string DisplayOrId => string.IsNullOrWhiteSpace(DisplayName) ? ModelId : DisplayName;
}

/// <summary>模型持久化文件结构（models.json 根对象）</summary>
public sealed class ModelsFile
{
    /// <summary>所有已保存模型</summary>
    public List<ModelConfig> Models { get; set; } = new();

    /// <summary>默认模型 Id（空=无）</summary>
    public string? DefaultModelId { get; set; }
}
