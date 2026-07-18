namespace LingFanEngine.Abstractions.Interfaces.Logging;

/// <summary>
/// 引擎日志上下文（不可变值类型语义）。
/// <para>携带场景名、标签、游戏时间等运行时上下文，With* 方法返回新实例。</para>
/// </summary>
public interface IEngineLogContext
{
    /// <summary>当前场景名（可选）</summary>
    string? Scene { get; }

    /// <summary>当前标签名（可选）</summary>
    string? Label { get; }

    /// <summary>当前游戏内时间（分钟，可选）</summary>
    long? GameTimeMinutes { get; }

    /// <summary>自定义属性字典（永不为 null，空时为空字典）</summary>
    IReadOnlyDictionary<string, object?> Properties { get; }

    /// <summary>上下文是否为空（所有字段均为默认值）</summary>
    bool IsEmpty { get; }

    /// <summary>设置场景名（返回新实例）</summary>
    IEngineLogContext WithScene(string? scene);

    /// <summary>设置标签名（返回新实例）</summary>
    IEngineLogContext WithLabel(string? label);

    /// <summary>设置游戏时间（返回新实例）</summary>
    IEngineLogContext WithGameTime(long? minutes);

    /// <summary>设置自定义属性（返回新实例）</summary>
    IEngineLogContext WithProperty(string key, object? value);

    /// <summary>
    /// 构建扁平化属性字典（包含 Scene/Label/GameTime + 自定义属性）。
    /// <para>用于将上下文注入日志条目。空上下文返回 null。</para>
    /// </summary>
    IReadOnlyDictionary<string, object?>? BuildProperties();

    /// <summary>
    /// 推入 AsyncLocal 上下文栈，返回 IDisposable 用于 using 块恢复。
    /// <para>using 块结束时自动恢复到前一个上下文。</para>
    /// </summary>
    IDisposable BeginScope();
}
