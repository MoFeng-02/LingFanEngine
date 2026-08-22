using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.AI;

/// <summary>
/// 模型客户端基类——持有配置、统一经 <see cref="IHttpClientFactory"/> 取 HttpClient（禁止内部 new），
/// 对 429/5xx 做指数退避重试（<see cref="RetryPolicy"/>），提供超时/鉴权辅助。
/// </summary>
public abstract class ModelClientBase : IModelClient
{
    private static readonly RetryPolicy s_retry = new();

    /// <summary>模型配置</summary>
    protected readonly ModelConfig Config;

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>真子类 provider 标识</summary>
    public abstract string Provider { get; }

    /// <summary>是否支持流式</summary>
    public virtual bool SupportsStreaming => false;

    /// <summary>是否支持工具调用</summary>
    public virtual bool SupportsTools => false;

    protected ModelClientBase(ModelConfig config, IHttpClientFactory httpClientFactory)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>从工厂取一个客户端实例（复用连接池）。</summary>
    protected HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, Config.Advanced.TimeoutSeconds));
        return client;
    }

    /// <summary>
    /// 带重试的发送——对 429/5xx 指数退避重试；每次尝试重建请求（HttpRequestMessage 不可复用）。
    /// 成功后返回最终响应（调用方负责 Dispose）。
    /// </summary>
    protected static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client, Func<HttpRequestMessage> createRequest, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = createRequest();
            var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!s_retry.ShouldRetry((int)resp.StatusCode, attempt))
                return resp;
            resp.Dispose();
            ct.ThrowIfCancellationRequested();
            await s_retry.DelayAsync(attempt, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public abstract Task<ModelResponse?> CompleteAsync(IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, CancellationToken ct);

    /// <inheritdoc/>
    public abstract IAsyncEnumerable<ModelChunk> CompleteStreamAsync(IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, CancellationToken ct);

    /// <summary>回调用量（如果有回调）。</summary>
    protected static void ReportUsage(ModelRequestOptions options, int input, int output)
        => options.UsageCallback?.Invoke(new UsageStats { InputTokens = input, OutputTokens = output, RequestCount = 1 });

    /// <summary>把工具定义拼成 JSON 数组文本（OpenAI 与 Anthropic 结构不同，由子类各自拼装）。</summary>
    protected static string BuildToolsJson(IReadOnlyList<ModelTool>? tools, bool anthropic)
    {
        if (tools == null || tools.Count == 0)
            return "";
        var sb = new StringBuilder("[");
        for (var i = 0; i < tools.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var t = tools[i];
            if (anthropic)
            {
                sb.Append("{\"name\":").Append(JsonSerializer.Serialize(t.Name, SdkJsonContext.Default.String))
                  .Append(",\"description\":").Append(JsonSerializer.Serialize(t.Description, SdkJsonContext.Default.String))
                  .Append(",\"input_schema\":").Append(t.ParametersJson).Append('}');
            }
            else
            {
                sb.Append("{\"type\":\"function\",\"function\":{\"name\":").Append(JsonSerializer.Serialize(t.Name, SdkJsonContext.Default.String))
                  .Append(",\"description\":").Append(JsonSerializer.Serialize(t.Description, SdkJsonContext.Default.String))
                  .Append(",\"parameters\":").Append(t.ParametersJson).Append("}}");
            }
        }
        sb.Append(']');
        return sb.ToString();
    }
}

/// <summary>OpenAI Chat Completions 兼容客户端（OpenAI / DeepSeek / Qwen / Ollama 等）。</summary>
public sealed class OpenAiCompatibleClient : ModelClientBase
{
    public override string Provider => "OpenAi";
    public override bool SupportsStreaming => true;
    public override bool SupportsTools => true;

    public OpenAiCompatibleClient(ModelConfig config, IHttpClientFactory httpClientFactory)
        : base(config, httpClientFactory) { }

    /// <inheritdoc/>
    public override async Task<ModelResponse?> CompleteAsync(
        IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, CancellationToken ct)
    {
        var url = Config.BaseUrl.TrimEnd('/') + "/chat/completions";
        var body = BuildBody(messages, options, stream: false);

        using var client = CreateClient();
        using var resp = await SendWithRetryAsync(client, () => BuildRequest(HttpMethod.Post, url, body), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        ReportUsage(options, ExtractUsage(root, "prompt_tokens"), ExtractUsage(root, "completion_tokens"));
        var message = choices[0].GetProperty("message");

        string? content = null;
        if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            content = contentEl.GetString();

        IReadOnlyList<ModelToolCall>? calls = null;
        if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            var list = new List<ModelToolCall>();
            foreach (var call in toolCallsEl.EnumerateArray())
            {
                if (call.TryGetProperty("id", out var idEl) && call.TryGetProperty("function", out var fn)
                    && fn.TryGetProperty("name", out var nameEl))
                {
                    var args = fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String
                        ? argsEl.GetString() ?? ""
                        : "{}";
                    list.Add(new ModelToolCall(
                        idEl.ValueKind == JsonValueKind.String ? idEl.GetString() ?? "" : "",
                        nameEl.GetString() ?? "",
                        args));
                }
            }
            if (list.Count > 0)
                calls = list;
        }
        return new ModelResponse(content, calls);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ModelChunk> CompleteStreamAsync(
        IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var url = Config.BaseUrl.TrimEnd('/') + "/chat/completions";
        var body = BuildBody(messages, options, stream: true);

        using var client = CreateClient();
        using var resp = await SendWithRetryAsync(client, () => BuildRequest(HttpMethod.Post, url, body), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            yield break;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = line.Substring(5).Trim();
            if (data == "[DONE]")
                break;
            ModelChunk? toYield = null;
            try
            {
                using var chunk = JsonDocument.Parse(data);
                var root = chunk.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    toYield = new ModelChunk(content.GetString());
                }
            }
            catch (JsonException)
            {
                // 忽略单行解析失败
            }
            if (toYield != null)
                yield return toYield;
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string body)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(Config.ApiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.ApiKey);
        return req;
    }

    private string BuildBody(IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, bool stream)
    {
        var sb = new StringBuilder("{\"model\":");
        sb.Append(JsonSerializer.Serialize(Config.ModelId, SdkJsonContext.Default.String));
        if (options.Temperature.HasValue)
            sb.Append(",\"temperature\":").Append(options.Temperature.Value.ToString("0.0#", CultureInfo.InvariantCulture));
        if (options.MaxTokens.HasValue)
            sb.Append(",\"max_tokens\":").Append(options.MaxTokens.Value);
        if (options.JsonMode)
            sb.Append(",\"response_format\":{\"type\":\"json_object\"}");
        if (stream)
            sb.Append(",\"stream\":true");
        var toolsJson = BuildToolsJson(options.Tools, anthropic: false);
        if (toolsJson.Length > 0)
            sb.Append(",\"tools\":").Append(toolsJson);
        sb.Append(",\"messages\":[");
        for (var i = 0; i < messages.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendMessage(sb, messages[i]);
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static void AppendMessage(StringBuilder sb, ModelMessage m)
    {
        sb.Append("{\"role\":").Append(JsonSerializer.Serialize(m.Role, SdkJsonContext.Default.String));

        // 工具结果消息：role=tool + tool_call_id
        if (m.ToolResultId != null)
        {
            sb.Append(",\"tool_call_id\":").Append(JsonSerializer.Serialize(m.ToolResultId, SdkJsonContext.Default.String))
              .Append(",\"content\":").Append(JsonSerializer.Serialize(m.Content, SdkJsonContext.Default.String))
              .Append('}');
            return;
        }

        sb.Append(",\"content\":").Append(JsonSerializer.Serialize(m.Content, SdkJsonContext.Default.String));

        // 助手携带工具调用
        if (m.ToolCalls is { Count: > 0 })
        {
            sb.Append(",\"tool_calls\":[");
            for (var i = 0; i < m.ToolCalls.Count; i++)
            {
                var tc = m.ToolCalls[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(JsonSerializer.Serialize(tc.Id, SdkJsonContext.Default.String))
                  .Append(",\"type\":\"function\",\"function\":{\"name\":").Append(JsonSerializer.Serialize(tc.Name, SdkJsonContext.Default.String))
                  .Append(",\"arguments\":").Append(JsonSerializer.Serialize(tc.ArgumentsJson, SdkJsonContext.Default.String))
                  .Append("}}");
            }
            sb.Append(']');
        }
        sb.Append('}');
    }

    private static int ExtractUsage(JsonElement root, string key)
    {
        if (!root.TryGetProperty("usage", out var usage)) return 0;
        if (usage.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt32(out var n) ? n : 0;
        return 0;
    }
}

/// <summary>Anthropic Messages 客户端（Claude 系列 / 兼容 Messages 协议端点）。</summary>
public sealed class AnthropicClient : ModelClientBase
{
    public override string Provider => "Anthropic";
    public override bool SupportsStreaming => true;
    public override bool SupportsTools => true;

    public AnthropicClient(ModelConfig config, IHttpClientFactory httpClientFactory)
        : base(config, httpClientFactory) { }

    /// <inheritdoc/>
    public override async Task<ModelResponse?> CompleteAsync(
        IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, CancellationToken ct)
    {
        var url = Config.BaseUrl.TrimEnd('/') + "/v1/messages";
        var body = BuildBody(messages, options, stream: false);

        using var client = CreateClient();
        using var resp = await SendWithRetryAsync(client, () => BuildRequest(HttpMethod.Post, url, body), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("content", out var content) || content.GetArrayLength() == 0)
            return null;

        ReportUsage(options, ExtractUsage(root, "input_tokens"), ExtractUsage(root, "output_tokens"));

        var textSb = new StringBuilder();
        List<ModelToolCall>? calls = null;
        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl)) continue;
            var type = typeEl.GetString();
            if (type == "text" && block.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                textSb.Append(textEl.GetString());
            else if (type == "tool_use" && block.TryGetProperty("id", out var idEl) && block.TryGetProperty("name", out var nameEl))
            {
                calls ??= new List<ModelToolCall>();
                var input = block.TryGetProperty("input", out var inputEl) && inputEl.ValueKind != JsonValueKind.Undefined
                    ? inputEl.GetRawText()
                    : "{}";
                calls.Add(new ModelToolCall(
                    idEl.ValueKind == JsonValueKind.String ? idEl.GetString() ?? "" : "",
                    nameEl.GetString() ?? "",
                    input));
            }
        }
        var contentText = textSb.Length > 0 ? textSb.ToString() : null;
        return new ModelResponse(contentText, calls);
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ModelChunk> CompleteStreamAsync(
        IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var url = Config.BaseUrl.TrimEnd('/') + "/v1/messages";
        var body = BuildBody(messages, options, stream: true);

        using var client = CreateClient();
        using var resp = await SendWithRetryAsync(client, () => BuildRequest(HttpMethod.Post, url, body), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            yield break;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = line.Substring(5).Trim();
            ModelChunk? toYield = null;
            try
            {
                using var chunk = JsonDocument.Parse(data);
                var root = chunk.RootElement;
                if (root.TryGetProperty("type", out var type) && type.GetString() == "content_block_delta"
                    && root.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    toYield = new ModelChunk(text.GetString());
                }
            }
            catch (JsonException)
            {
                // 忽略单行解析失败
            }
            if (toYield != null)
                yield return toYield;
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string body)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(Config.ApiKey))
            req.Headers.TryAddWithoutValidation("x-api-key", Config.ApiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        return req;
    }

    private string BuildBody(IReadOnlyList<ModelMessage> messages, ModelRequestOptions options, bool stream)
    {
        // system 提升为顶级参数；其余消息转 content blocks（工具结果用 user/tool_result 包裹满足交替）。
        var systemParts = new List<string>();
        var msgsSb = new StringBuilder();
        var hasMsg = false;
        foreach (var m in messages)
        {
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(m.Content))
                    systemParts.Add(m.Content);
                continue;
            }
            if (hasMsg) msgsSb.Append(',');
            AppendAnthropicMessage(msgsSb, m);
            hasMsg = true;
        }

        var sb = new StringBuilder("{\"model\":").Append(JsonSerializer.Serialize(Config.ModelId, SdkJsonContext.Default.String));
        if (systemParts.Count > 0)
            sb.Append(",\"system\":").Append(JsonSerializer.Serialize(string.Join("\n\n", systemParts), SdkJsonContext.Default.String));
        sb.Append(",\"max_tokens\":").Append(options.MaxTokens ?? 2048);
        if (options.Temperature.HasValue)
            sb.Append(",\"temperature\":").Append(options.Temperature.Value.ToString("0.0#", CultureInfo.InvariantCulture));
        if (stream)
            sb.Append(",\"stream\":true");
        var toolsJson = BuildToolsJson(options.Tools, anthropic: true);
        if (toolsJson.Length > 0)
            sb.Append(",\"tools\":").Append(toolsJson);
        sb.Append(",\"messages\":[").Append(msgsSb).Append("]}");
        return sb.ToString();
    }

    private static void AppendAnthropicMessage(StringBuilder sb, ModelMessage m)
    {
        // 工具结果：作为 user 消息的 tool_result 内容块（满足 assistant(tool_use) → user(tool_result) 交替）
        if (m.ToolResultId != null)
        {
            sb.Append("{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":")
              .Append(JsonSerializer.Serialize(m.ToolResultId, SdkJsonContext.Default.String))
              .Append(",\"content\":").Append(JsonSerializer.Serialize(m.Content, SdkJsonContext.Default.String))
              .Append("}]}");
            return;
        }

        var role = m.Role;

        // 助手携带工具调用：content 块 = text(如有) + tool_use 块
        if (m.ToolCalls is { Count: > 0 })
        {
            var blocks = new StringBuilder("[");
            var hasBlock = false;
            if (!string.IsNullOrEmpty(m.Content))
            {
                blocks.Append("{\"type\":\"text\",\"text\":").Append(JsonSerializer.Serialize(m.Content, SdkJsonContext.Default.String)).Append('}');
                hasBlock = true;
            }
            foreach (var tc in m.ToolCalls)
            {
                if (hasBlock) blocks.Append(',');
                blocks.Append("{\"type\":\"tool_use\",\"id\":").Append(JsonSerializer.Serialize(tc.Id, SdkJsonContext.Default.String))
                       .Append(",\"name\":").Append(JsonSerializer.Serialize(tc.Name, SdkJsonContext.Default.String))
                       .Append(",\"input\":").Append(tc.ArgumentsJson).Append('}');
                hasBlock = true;
            }
            blocks.Append(']');
            sb.Append("{\"role\":").Append(JsonSerializer.Serialize(role, SdkJsonContext.Default.String))
              .Append(",\"content\":").Append(blocks).Append('}');
            return;
        }

        sb.Append("{\"role\":").Append(JsonSerializer.Serialize(role, SdkJsonContext.Default.String))
          .Append(",\"content\":").Append(JsonSerializer.Serialize(m.Content, SdkJsonContext.Default.String))
          .Append('}');
    }

    private static int ExtractUsage(JsonElement root, string key)
    {
        if (!root.TryGetProperty("usage", out var usage)) return 0;
        if (usage.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.TryGetInt32(out var n) ? n : 0;
        return 0;
    }
}