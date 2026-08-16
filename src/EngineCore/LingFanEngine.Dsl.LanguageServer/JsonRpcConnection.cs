using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LingFanEngine.Dsl.LanguageServer.Protocol;

namespace LingFanEngine.Dsl.LanguageServer;

/// <summary>
/// 极简 JSON-RPC 2.0 over stdio 传输层（LSP 协议信封）。
/// <para>只读 <see cref="Request"/> / 只写 <see cref="Response"/> 与 <see cref="Notification"/>；全部经 <see cref="LspJsonContext"/> 源生成序列化，零反射，AOT 安全。</para>
/// <para>日志走标准错误流（Console.Error），与协议标准输出严格隔离。</para>
/// </summary>
internal sealed class JsonRpcConnection : IDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly object _writeLock = new();

    public JsonRpcConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>读取下一条消息；流结束返回 null。</summary>
    public Request? ReadMessage()
    {
        int contentLength = -1;
        while (true)
        {
            var line = ReadAsciiLine();
            if (line == null) return null; // 流结束
            if (line.Length == 0) break;   // 头部结束（空行）

            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                var name = line.Substring(0, idx).Trim();
                var value = line.Substring(idx + 1).Trim();
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(value, out var len))
                    contentLength = len;
            }
        }

        if (contentLength < 0) return null;
        var body = new byte[contentLength];
        ReadExactly(body, contentLength);
        var json = Encoding.UTF8.GetString(body);
        return JsonSerializer.Deserialize(json, LspJsonContext.Default.Request);
    }

    /// <summary>发送成功响应（result 经指定类型信息序列化为 JSON 元素）。</summary>
    public void SendResult(JsonElement id, object? result, System.Text.Json.Serialization.Metadata.JsonTypeInfo? resultType)
    {
        JsonElement? elem = null;
        if (result != null && resultType != null)
            elem = JsonSerializer.SerializeToElement(result, resultType);

        var response = new Response { Id = id, Result = elem };
        WriteMessage(response, LspJsonContext.Default.Response);
    }

    /// <summary>发送错误响应。</summary>
    public void SendError(JsonElement id, int code, string message)
    {
        var error = new RpcError { Code = code, Message = message };
        var elem = JsonSerializer.SerializeToElement(error, LspJsonContext.Default.RpcError);
        var response = new Response { Id = id, Error = elem };
        WriteMessage(response, LspJsonContext.Default.Response);
    }

    /// <summary>发送服务端→客户端通知（如 publishDiagnostics）。</summary>
    public void SendNotification(string method, object @params, System.Text.Json.Serialization.Metadata.JsonTypeInfo paramsType)
    {
        var elem = JsonSerializer.SerializeToElement(@params, paramsType);
        var notification = new Notification { Method = method, Params = elem };
        WriteMessage(notification, LspJsonContext.Default.Notification);
    }

    private void WriteMessage(object message, System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo)
    {
        var json = JsonSerializer.Serialize(message, typeInfo);
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeLock)
        {
            _output.Write(header, 0, header.Length);
            _output.Write(body, 0, body.Length);
            _output.Flush();
        }
    }

    private string? ReadAsciiLine()
    {
        var sb = new StringBuilder();
        while (true)
        {
            int b = _input.ReadByte();
            if (b < 0) return sb.Length == 0 ? null : sb.ToString();
            if (b == '\n')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] == '\r') sb.Length--;
                return sb.ToString();
            }
            sb.Append((char)b);
        }
    }

    private void ReadExactly(byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var n = _input.Read(buffer, offset, count - offset);
            if (n <= 0) throw new EndOfStreamException("LSP stream ended mid-message");
            offset += n;
        }
    }

    public void Dispose()
    {
        try { _output.Dispose(); } catch { /* ignore */ }
        try { _input.Dispose(); } catch { /* ignore */ }
    }
}
