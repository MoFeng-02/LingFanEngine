namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 引擎日志级别
/// </summary>
public enum EngineLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// 引擎日志接口——替代散落的 Debug.WriteLine，支持多 sink 和级别过滤。
/// <para>默认实现 DebugEngineLogger 在 Debug 模式输出到 Debug.WriteLine，Release 模式静默。</para>
/// <para>游戏可通过 DI 替换为文件日志/控制台日志等实现。</para>
/// </summary>
public interface IEngineLogger
{
    void Log(EngineLogLevel level, string message, Exception? exception = null);
}

/// <summary>
/// 扩展方法——提供分类便捷调用
/// </summary>
public static class EngineLoggerExtensions
{
    public static void LogDebug(this IEngineLogger logger, string message)
        => logger.Log(EngineLogLevel.Debug, message);

    public static void LogInfo(this IEngineLogger logger, string message)
        => logger.Log(EngineLogLevel.Info, message);

    public static void LogWarning(this IEngineLogger logger, string message)
        => logger.Log(EngineLogLevel.Warning, message);

    public static void LogError(this IEngineLogger logger, string message, Exception? ex = null)
        => logger.Log(EngineLogLevel.Error, message, ex);
}
