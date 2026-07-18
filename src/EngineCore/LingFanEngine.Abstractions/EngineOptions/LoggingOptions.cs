using LingFanEngine.Abstractions.Interfaces.Logging;

namespace LingFanEngine.Abstractions.EngineOptions;

/// <summary>
/// 日志 Sink 组合标志——控制启用的日志输出目标。
/// </summary>
[Flags]
public enum LoggingSinks
{
    /// <summary>不启用任何 Sink</summary>
    None = 0,

    /// <summary>Debug/Trace 输出（IDE 调试器附加时可见）</summary>
    DebugTrace = 1,

    /// <summary>标准控制台输出（带彩色）</summary>
    Console = 2,

    /// <summary>文件输出（按天滚动，自动清理过期文件）</summary>
    File = 4,

    /// <summary>启用全部 Sink</summary>
    All = DebugTrace | Console | File
}

/// <summary>
/// 引擎日志配置选项。
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>
    /// 最低日志级别。低于此级别的日志被丢弃（零开销）。
    /// <para>Debug 编译默认 Debug，Release 编译默认 Info。</para>
    /// </summary>
#if DEBUG
    public EngineLogLevel MinimumLevel { get; set; } = EngineLogLevel.Debug;
#else
    public EngineLogLevel MinimumLevel { get; set; } = EngineLogLevel.Info;
#endif

    /// <summary>
    /// 启用的 Sink 组合（默认仅 DebugTrace）。
    /// <para>游戏层可设为 All 启用全部输出目标。</para>
    /// <para>WASM 平台 File Sink 会被自动剔除（文件系统不可用）。</para>
    /// </summary>
    public LoggingSinks Sinks { get; set; } = LoggingSinks.DebugTrace;

    /// <summary>
    /// 文件日志保留天数（默认 7）。
    /// <para>启动时自动清理超过此天数的日志文件。</para>
    /// <para>仅在 Sinks 包含 File 时生效。</para>
    /// </summary>
    public int FileRetentionDays { get; set; } = 7;

    /// <summary>
    /// 是否将 Warning 及以上级别的引擎日志镜像到游戏内调试面板（IDebugConsoleService）。
    /// <para>默认 true——让开发者在游戏内即可看到引擎警告。</para>
    /// <para>与 IDebugConsoleService 的 debug 命令日志互补，不冲突。</para>
    /// </summary>
    public bool MirrorToDebugConsole { get; set; } = true;

    /// <summary>
    /// 镜像到游戏内调试面板的最低级别（默认 Warning）。
    /// <para>仅 MirrorToDebugConsole=true 时生效。</para>
    /// </summary>
    public EngineLogLevel MirrorMinimumLevel { get; set; } = EngineLogLevel.Warning;
}
