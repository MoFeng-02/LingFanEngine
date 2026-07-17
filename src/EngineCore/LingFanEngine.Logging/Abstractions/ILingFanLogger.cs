using Microsoft.Extensions.Logging;

namespace LingFanEngine.Logging.Abstractions;

/// <summary>
/// 灵泛引擎高性能日志接口
/// </summary>
public interface ILingFanLogger
{
    /// <summary>
    /// 记录日志（高 GC 压力版本，仅用于非频繁路径）
    /// </summary>
    void Log(LogLevel level, EventId eventId, string message, params object?[] args);

    /// <summary>
    /// 记录异常
    /// </summary>
    void LogError(Exception exception, string message, params object?[] args);

    /// <summary>
    /// 记录警告
    /// </summary>
    void LogWarning(string message, params object?[] args);

    /// <summary>
    /// 记录信息
    /// </summary>
    void LogInformation(string message, params object?[] args);

    /// <summary>
    /// 记录调试
    /// </summary>
    void LogDebug(string message, params object?[] args);

    /// <summary>
    /// 记录跟踪
    /// </summary>
    void LogTrace(string message, params object?[] args);

    /// <summary>
    /// 是否启用指定级别
    /// </summary>
    bool IsEnabled(LogLevel level);

    /// <summary>
    /// 创建作用域（用于上下文关联）
    /// </summary>
    IDisposable? BeginScope<TState>(TState state) where TState : notnull;
}

/// <summary>
/// 泛型版本，自动带上类别名
/// </summary>
public interface ILingFanLogger<out TCategory> : ILingFanLogger;