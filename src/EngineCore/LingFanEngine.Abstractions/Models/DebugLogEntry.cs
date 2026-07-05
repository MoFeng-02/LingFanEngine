namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 调试日志条目
/// <para>由 DebugConsoleService 记录，UI 层通过 StateKeys.Debug.Logs 读取列表渲染调试面板。</para>
/// </summary>
public class DebugLogEntry
{
    /// <summary>日志级别</summary>
    public string Level { get; set; } = "Info";

    /// <summary>日志消息</summary>
    public string Message { get; set; } = "";

    /// <summary>来源（命令名/场景名等）</summary>
    public string? Source { get; set; }

    /// <summary>记录时间戳</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
