using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Logging.Sinks;

/// <summary>
/// 游戏内调试面板镜像 Sink——将引擎日志镜像到 IDebugConsoleService。
/// <para>让开发者在游戏内调试面板中也能看到引擎的 Warning/Error 日志。</para>
/// <para>仅镜像指定级别及以上的日志（默认 Warning+），避免 Info/Debug 刷屏。</para>
/// <para>与 IDebugConsoleService 的 debug 命令日志互补，不冲突：</para>
/// <para>- debug 命令日志：DSL 脚本主动记录，面向脚本调试</para>
/// <para>- 引擎日志镜像：引擎自身诊断，面向引擎问题排查</para>
/// <para>无状态，无需 lock（IDebugConsoleService 内部已做线程安全）。</para>
/// </summary>
internal sealed class DebugConsoleMirrorSink : IEngineLogSink
{
    private readonly IDebugConsoleService _console;
    private readonly EngineLogLevel _minimumLevel;

    /// <summary>
    /// 创建调试面板镜像 Sink。
    /// </summary>
    /// <param name="console">游戏内调试控制台服务</param>
    /// <param name="minimumLevel">镜像的最低日志级别</param>
    public DebugConsoleMirrorSink(IDebugConsoleService console, EngineLogLevel minimumLevel)
    {
        _console = console;
        _minimumLevel = minimumLevel;
    }

    public void Write(EngineLogEntry entry)
    {
        if (entry.Level < _minimumLevel)
            return;

        // 格式化消息：[分类] 消息
        var message = entry.ExceptionText is not null
            ? $"[{entry.Category}] {entry.Message} — {entry.ExceptionText}"
            : $"[{entry.Category}] {entry.Message}";

        _console.Log(entry.Level.ToString(), message, entry.Category);
    }

    public void Flush() { /* 无缓冲 */ }

    public void Dispose() { /* 无资源 */ }
}
