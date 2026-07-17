using System;
using System.Collections.Generic;
using System.Threading;
using LingFanEngine.Logging.Abstractions;

namespace LingFanEngine.Logging.Core;

/// <summary>
/// 日志上下文访问器（基于 AsyncLocal，支持异步流转）
/// </summary>
public sealed class LogContextAccessor : ILogContextAccessor
{
    private static readonly AsyncLocal<LogContextHolder> _currentContext = new();

    public ILogContext Current => _currentContext.Value?.Context ?? EmptyContext.Instance;

    internal static IDisposable BeginScope(ILogContext context)
    {
        var oldHolder = _currentContext.Value;
        _currentContext.Value = new LogContextHolder(context);
        return new ContextScopeDisposable(oldHolder);
    }

    private sealed class LogContextHolder
    {
        public ILogContext Context { get; }

        public LogContextHolder(ILogContext context)
        {
            Context = context;
        }
    }

    private sealed class ContextScopeDisposable : IDisposable
    {
        private readonly LogContextHolder? _previous;

        public ContextScopeDisposable(LogContextHolder? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            _currentContext.Value = _previous ?? throw new("未初始化LogContextHolder");
        }
    }
}

/// <summary>
/// 日志上下文实现（不可变）
/// </summary>
public sealed class LogContext : ILogContext
{
    public string? Scene { get; }
    public long? GameTime { get; }
    public string? AnchorId { get; }
    public string? PlayerAction { get; }
    public IReadOnlyDictionary<string, object> Properties { get; }

    private static readonly IReadOnlyDictionary<string, object> EmptyProperties =
        new Dictionary<string, object>();

    public LogContext()
    {
        Properties = EmptyProperties;
    }

    private LogContext(
        string? scene,
        long? gameTime,
        string? anchorId,
        string? playerAction,
        IReadOnlyDictionary<string, object> properties)
    {
        Scene = scene;
        GameTime = gameTime;
        AnchorId = anchorId;
        PlayerAction = playerAction;
        Properties = properties;
    }

    public ILogContext WithScene(string scene)
    {
        return new LogContext(scene, GameTime, AnchorId, PlayerAction, Properties);
    }

    public ILogContext WithGameTime(long time)
    {
        return new LogContext(Scene, time, AnchorId, PlayerAction, Properties);
    }

    public ILogContext WithAnchor(string anchorId)
    {
        return new LogContext(Scene, GameTime, anchorId, PlayerAction, Properties);
    }

    public ILogContext WithPlayerAction(string action)
    {
        return new LogContext(Scene, GameTime, AnchorId, action, Properties);
    }

    public ILogContext WithProperty(string key, object value)
    {
        var newProps = new Dictionary<string, object>(Properties);
        newProps[key] = value;
        return new LogContext(Scene, GameTime, AnchorId, PlayerAction, newProps);
    }

    public IDisposable BeginScope()
    {
        return LogContextAccessor.BeginScope(this);
    }

}

internal sealed class EmptyContext : ILogContext
{
    public static EmptyContext Instance { get; } = new();

    public string? Scene => null;
    public long? GameTime => null;
    public string? AnchorId => null;
    public string? PlayerAction => null;
    public IReadOnlyDictionary<string, object> Properties =>
        new Dictionary<string, object>();

    public ILogContext WithScene(string scene) => new LogContext().WithScene(scene);
    public ILogContext WithGameTime(long time) => new LogContext().WithGameTime(time);
    public ILogContext WithAnchor(string anchorId) => new LogContext().WithAnchor(anchorId);
    public ILogContext WithPlayerAction(string action) => new LogContext().WithPlayerAction(action);
    public ILogContext WithProperty(string key, object value) => new LogContext().WithProperty(key, value);
    public IDisposable BeginScope() => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static NullDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}