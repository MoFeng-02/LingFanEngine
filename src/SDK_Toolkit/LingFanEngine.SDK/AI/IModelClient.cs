using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LingFanEngine.SDK.AI;

/// <summary>对话消息（role + content；工具调用结果可选）</summary>
public sealed record ModelMessage(string Role, string Content, IReadOnlyList<ModelToolCall>? ToolCalls = null, string? ToolResultId = null);

/// <summary>模型发起的工具调用（Function Calling 的"模型要执行的调用"）</summary>
public sealed record ModelToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>可提供给模型的工具定义（Name/Description + JSON Schema 参数）。</summary>
public sealed record ModelTool(string Name, string Description, string ParametersJson);

/// <summary>单轮响应：文本内容（可为空）+ 模型要发起的工具调用（可为空）。</summary>
public sealed record ModelResponse(string? Content, IReadOnlyList<ModelToolCall>? ToolCalls);

/// <summary>流式增量片段</summary>
public sealed record ModelChunk(string? Text);

/// <summary>请求选项：温度 / 上限 / JSON 模式 / 工具 / 用量回调</summary>
public sealed class ModelRequestOptions
{
    /// <summary>温度（null = 用 provider 默认）</summary>
    public decimal? Temperature { get; init; }

    /// <summary>最大输出 token（null = 用 provider 默认）</summary>
    public int? MaxTokens { get; init; }

    /// <summary>是否要求 JSON 结构化输出</summary>
    public bool JsonMode { get; init; }

    /// <summary>可供模型调用的工具（Function Calling）</summary>
    public IReadOnlyList<ModelTool>? Tools { get; init; }

    /// <summary>每次响应后回调用量（供累计/成本展示）</summary>
    public Action<LingFanEngine.SDK.Models.UsageStats>? UsageCallback { get; init; }
}

/// <summary>
/// 统一 AI 模型客户端——屏蔽 OpenAI 兼容 / Anthropic 协议差异，只暴露语义 API。
/// <para>实现：<see cref="OpenAiCompatibleClient"/>（OpenAI/DeepSeek/Qwen/Ollama 兼容）/ <see cref="AnthropicClient"/>（Claude）。</para>
/// <para>传递纪律：所有实现经 <see cref="System.Net.Http.IHttpClientFactory"/> 获取 HttpClient，禁止内部 new。</para>
/// </summary>
public interface IModelClient
{
    /// <summary>provider 标识（OpenAi / Anthropic）</summary>
    string Provider { get; }

    /// <summary>是否支持流式（SSE）</summary>
    bool SupportsStreaming { get; }

    /// <summary>是否支持工具调用（Function Calling）</summary>
    bool SupportsTools { get; }

    /// <summary>单轮补全（返回文本 + 可选工具调用；失败返回 null）</summary>
    Task<ModelResponse?> CompleteAsync(IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, CancellationToken ct);

    /// <summary>流式补全（逐 token 产出；不支持时回退为一次性整段）</summary>
    IAsyncEnumerable<ModelChunk> CompleteStreamAsync(IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, CancellationToken ct);
}