using System.Runtime.CompilerServices;

namespace LingFanEngine.Abstractions.Interfaces.Logging;

/// <summary>
/// 日志便捷扩展方法——提供级别快捷调用。
/// <para>Caller 信息通过扩展方法透传，保留调用方定位。</para>
/// </summary>
public static class EngineLoggerExtensions
{
    /// <summary>记录 Trace 级别日志</summary>
    public static void LogTrace(
        this IEngineLogger logger, string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => logger.Log(EngineLogLevel.Trace, message, null, member, file, line);

    /// <summary>记录 Debug 级别日志</summary>
    public static void LogDebug(
        this IEngineLogger logger, string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => logger.Log(EngineLogLevel.Debug, message, null, member, file, line);

    /// <summary>记录 Info 级别日志</summary>
    public static void LogInfo(
        this IEngineLogger logger, string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => logger.Log(EngineLogLevel.Info, message, null, member, file, line);

    /// <summary>记录 Warning 级别日志</summary>
    public static void LogWarning(
        this IEngineLogger logger, string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => logger.Log(EngineLogLevel.Warning, message, null, member, file, line);

    /// <summary>记录 Error 级别日志（可附带异常）</summary>
    public static void LogError(
        this IEngineLogger logger, string message, Exception? exception = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => logger.Log(EngineLogLevel.Error, message, exception, member, file, line);

    /// <summary>记录 Critical 级别日志（可附带异常）</summary>
    public static void LogCritical(
        this IEngineLogger logger, string message, Exception? exception = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => logger.Log(EngineLogLevel.Critical, message, exception, member, file, line);
}
