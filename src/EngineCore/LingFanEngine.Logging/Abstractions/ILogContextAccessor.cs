namespace LingFanEngine.Logging.Abstractions;

/// <summary>
/// 日志上下文访问器
/// </summary>
public interface ILogContextAccessor
{
    /// <summary>
    /// 当前日志上下文
    /// </summary>
    ILogContext Current { get; }
}

/// <summary>
/// 日志上下文（不可变）
/// </summary>
public interface ILogContext
{
    /// <summary>
    /// 当前场景名
    /// </summary>
    string? Scene { get; }

    /// <summary>
    /// 当前游戏内时间（分钟）
    /// </summary>
    long? GameTime { get; }

    /// <summary>
    /// 当前锚点ID
    /// </summary>
    string? AnchorId { get; }

    /// <summary>
    /// 当前玩家操作
    /// </summary>
    string? PlayerAction { get; }

    /// <summary>
    /// 自定义属性
    /// </summary>
    IReadOnlyDictionary<string, object> Properties { get; }

    /// <summary>
    /// 设置场景（返回新实例）
    /// </summary>
    ILogContext WithScene(string scene);

    /// <summary>
    /// 设置游戏时间（返回新实例）
    /// </summary>
    ILogContext WithGameTime(long time);

    /// <summary>
    /// 设置锚点（返回新实例）
    /// </summary>
    ILogContext WithAnchor(string anchorId);

    /// <summary>
    /// 设置玩家操作（返回新实例）
    /// </summary>
    ILogContext WithPlayerAction(string action);

    /// <summary>
    /// 设置自定义属性（返回新实例）
    /// </summary>
    ILogContext WithProperty(string key, object value);

    /// <summary>
    /// 开始一个作用域（用于 using 块）
    /// </summary>
    IDisposable BeginScope();
}