using System.Diagnostics;
using System.Text;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Logging.Sinks;

/// <summary>
/// Debug/Trace 输出 Sink——写入 System.Diagnostics.Debug（IDE 调试器附加时可见）。
/// <para>替代引擎中散落的 Debug.WriteLine 调用。</para>
/// <para>线程安全：lock 保护（Debug.WriteLine 本身可能非线程安全）。</para>
/// <para>AOT 友好：无反射，纯字符串操作。</para>
/// </summary>
internal sealed class DebugTraceSink : IEngineLogSink
{
    private readonly object _lock = new();

    public void Write(EngineLogEntry entry)
    {
        var line = FormatEntry(entry);
        lock (_lock)
        {
            Debug.WriteLine(line);
        }
    }

    public void Flush() { /* Debug.WriteLine 无缓冲 */ }

    public void Dispose() { /* 无资源 */ }

    /// <summary>
    /// 格式化日志条目为单行文本。
    /// <para>格式：[级别] [分类] 消息 — 异常 | 调用方信息</para>
    /// </summary>
    internal static string FormatEntry(EngineLogEntry entry)
    {
        var sb = new StringBuilder(128);
        sb.Append('[').Append(LevelToShortString(entry.Level)).Append("] ");
        sb.Append('[').Append(entry.Category).Append("] ");
        sb.Append(entry.Message);

        if (entry.ExceptionText is not null)
        {
            sb.Append(" — ").Append(entry.ExceptionText);
        }

        // 调用方信息（仅在有值时附加）
        if (entry.Member is { } member && member.Length > 0)
        {
            sb.Append(" | ").Append(member);
            if (entry.FilePath is { } file && file.Length > 0)
            {
                // 仅显示文件名，不显示完整路径
                var fileName = System.IO.Path.GetFileName(file);
                sb.Append(" @ ").Append(fileName).Append(':').Append(entry.LineNumber);
            }
        }

        // 上下文属性（仅在有值时附加）
        if (entry.Properties is { Count: > 0 } props)
        {
            sb.Append(" {");
            var first = true;
            foreach (var kv in props)
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            sb.Append('}');
        }

        return sb.ToString();
    }

    private static string LevelToShortString(EngineLogLevel level) => level switch
    {
        EngineLogLevel.Trace => "TRC",
        EngineLogLevel.Debug => "DBG",
        EngineLogLevel.Info => "INF",
        EngineLogLevel.Warning => "WRN",
        EngineLogLevel.Error => "ERR",
        EngineLogLevel.Critical => "CRT",
        _ => "???"
    };
}
