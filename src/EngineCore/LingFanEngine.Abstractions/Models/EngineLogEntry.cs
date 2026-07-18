using LingFanEngine.Abstractions.Interfaces.Logging;

namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 引擎诊断日志条目（不可变）。
/// <para>由 IEngineLogger 创建，传递给 IEngineLogSink 输出。</para>
/// <para>AOT 友好：纯数据 record，无反射。</para>
/// </summary>
public sealed record EngineLogEntry
{
    /// <summary>记录时间戳（UTC）</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>日志级别</summary>
    public EngineLogLevel Level { get; init; } = EngineLogLevel.Info;

    /// <summary>日志分类名（如 "StoryLoader"）</summary>
    public string Category { get; init; } = "";

    /// <summary>日志消息</summary>
    public string Message { get; init; } = "";

    /// <summary>异常完整文本（ex?.ToString()），AOT 安全</summary>
    public string? ExceptionText { get; init; }

    /// <summary>调用方成员名（编译器填充，可选）</summary>
    public string? Member { get; init; }

    /// <summary>调用方文件路径（编译器填充，可选）</summary>
    public string? FilePath { get; init; }

    /// <summary>调用方行号（编译器填充，0 = 未知）</summary>
    public int LineNumber { get; init; }

    /// <summary>上下文属性（场景/标签/游戏时间/自定义属性），空时为 null</summary>
    public IReadOnlyDictionary<string, object?>? Properties { get; init; }
}
