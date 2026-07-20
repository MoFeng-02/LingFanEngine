namespace LingFanEngine.SDK.Models;

/// <summary>游戏启动结果</summary>
public class RunResult
{
    /// <summary>整体是否成功（含启动或构建失败的统一判定）</summary>
    public bool Success { get; set; }

    /// <summary>是否真正启动了进程（false 时可能仅是构建失败）</summary>
    public bool Launched { get; set; }

    /// <summary>实际启动的可执行文件路径（成功时填充）</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>面向用户的状态消息</summary>
    public string Message { get; set; } = "";
}
