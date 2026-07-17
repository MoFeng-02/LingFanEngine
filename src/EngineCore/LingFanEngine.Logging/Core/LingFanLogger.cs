using System;
using System.Runtime.CompilerServices;
using LingFanEngine.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace LingFanEngine.Logging.Core;

/// <summary>
/// 高性能日志实现（无反射，AOT 友好）
/// </summary>
internal sealed class LingFanLogger<TCategory> : ILingFanLogger<TCategory>
{
    private readonly ILogger _logger;
    private readonly ILogContextAccessor? _contextAccessor;

    /// <summary>
    /// 类别名称（用于 Serilog 的 SourceContext）
    /// </summary>
    private static readonly string CategoryName = typeof(TCategory).FullName ?? typeof(TCategory).Name;

    public LingFanLogger(ILoggerFactory loggerFactory, ILogContextAccessor? contextAccessor = null)
    {
        _logger = loggerFactory.CreateLogger(CategoryName);
        _contextAccessor = contextAccessor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Log(LogLevel level, EventId eventId, string message, params object?[] args)
    {
        if (!IsEnabled(level)) return;

        var state = new LogState(level, eventId, message, args);
        using var scope = BeginScopeIfNeeded();
        _logger.Log(level, eventId, state, null, static (s, _) => s.ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogError(Exception exception, string message, params object?[] args)
    {
        if (!IsEnabled(LogLevel.Error)) return;

        using var scope = BeginScopeIfNeeded();
        _logger.LogError(exception, message, args);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogWarning(string message, params object?[] args)
    {
        if (!IsEnabled(LogLevel.Warning)) return;

        using var scope = BeginScopeIfNeeded();
        _logger.LogWarning(message, args);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogInformation(string message, params object?[] args)
    {
        if (!IsEnabled(LogLevel.Information)) return;

        using var scope = BeginScopeIfNeeded();
        _logger.LogInformation(message, args);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogDebug(string message, params object?[] args)
    {
        if (!IsEnabled(LogLevel.Debug)) return;

        using var scope = BeginScopeIfNeeded();
        _logger.LogDebug(message, args);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void LogTrace(string message, params object?[] args)
    {
        if (!IsEnabled(LogLevel.Trace)) return;

        using var scope = BeginScopeIfNeeded();
        _logger.LogTrace(message, args);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(LogLevel level)
    {
        return _logger.IsEnabled(ToMELogLevel(level));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _logger.BeginScope(state);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IDisposable? BeginScopeIfNeeded()
    {
        var context = _contextAccessor?.Current;
        if (context is null || IsEmptyContext(context))
        {
            return null;
        }

        return _logger.BeginScope(new ContextScopeState(context));
    }

    private static bool IsEmptyContext(ILogContext context)
    {
        return string.IsNullOrEmpty(context.Scene)
            && !context.GameTime.HasValue
            && string.IsNullOrEmpty(context.AnchorId)
            && string.IsNullOrEmpty(context.PlayerAction)
            && (context.Properties is null || context.Properties.Count == 0);
    }

    private static Microsoft.Extensions.Logging.LogLevel ToMELogLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
            LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            LogLevel.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.None
        };
    }

    /// <summary>
    /// 日志状态结构体（减少 GC 分配）
    /// </summary>
    private readonly record struct LogState(
        LogLevel Level,
        EventId EventId,
        string Message,
        object?[] Args);

    /// <summary>
    /// 上下文作用域状态
    /// </summary>
    private sealed class ContextScopeState : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly ILogContext _context;
        private KeyValuePair<string, object>[]? _cached;

        public ContextScopeState(ILogContext context)
        {
            _context = context;
        }

        public int Count => 0; // Serilog 通过 ForContext 方式注入

        public KeyValuePair<string, object> this[int index] => throw new IndexOutOfRangeException();

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            yield break;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

/// <summary>
/// 日志状态格式化器（AOT 友好，无反射）
/// </summary>
internal static class LogStateFormatter
{
    public static string FormatState<TState>(TState state, Exception? exception)
        where TState : notnull
    {
        return state.ToString() ?? string.Empty;
    }
}