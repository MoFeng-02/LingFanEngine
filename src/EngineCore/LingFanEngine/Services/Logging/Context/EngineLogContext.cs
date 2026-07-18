using LingFanEngine.Abstractions.Interfaces.Logging;

namespace LingFanEngine.Services.Logging.Context;

/// <summary>
/// 引擎日志上下文实现（不可变）。
/// <para>With* 方法返回新实例，原实例不受影响。</para>
/// <para>AOT 友好：无反射，纯数据操作。</para>
/// </summary>
internal sealed class EngineLogContext : IEngineLogContext
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyProperties =
        new Dictionary<string, object?>(0);

    /// <summary>空上下文单例（无任何上下文信息）</summary>
    public static readonly EngineLogContext Empty = new();

    /// <inheritdoc/>
    public string? Scene { get; }

    /// <inheritdoc/>
    public string? Label { get; }

    /// <inheritdoc/>
    public long? GameTimeMinutes { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> Properties { get; }

    private EngineLogContext(
        string? scene,
        string? label,
        long? gameTimeMinutes,
        IReadOnlyDictionary<string, object?> properties)
    {
        Scene = scene;
        Label = label;
        GameTimeMinutes = gameTimeMinutes;
        Properties = properties;
    }

    /// <summary>创建空上下文</summary>
    public EngineLogContext()
    {
        Properties = EmptyProperties;
    }

    /// <inheritdoc/>
    public bool IsEmpty =>
        Scene is null
        && Label is null
        && !GameTimeMinutes.HasValue
        && Properties.Count == 0;

    /// <inheritdoc/>
    public IEngineLogContext WithScene(string? scene) =>
        new EngineLogContext(scene, Label, GameTimeMinutes, Properties);

    /// <inheritdoc/>
    public IEngineLogContext WithLabel(string? label) =>
        new EngineLogContext(Scene, label, GameTimeMinutes, Properties);

    /// <inheritdoc/>
    public IEngineLogContext WithGameTime(long? minutes) =>
        new EngineLogContext(Scene, Label, minutes, Properties);

    /// <inheritdoc/>
    public IEngineLogContext WithProperty(string key, object? value)
    {
        var newProps = new Dictionary<string, object?>(Properties.Count + 1);
        // 先复制已有属性
        foreach (var kv in Properties)
            newProps[kv.Key] = kv.Value;
        // 再设置新 key（若同名则覆盖旧值）
        newProps[key] = value;

        return new EngineLogContext(Scene, Label, GameTimeMinutes, newProps);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?>? BuildProperties()
    {
        if (IsEmpty)
            return null;

        var dict = new Dictionary<string, object?>(4 + Properties.Count);

        if (Scene is not null)
            dict["Scene"] = Scene;
        if (Label is not null)
            dict["Label"] = Label;
        if (GameTimeMinutes.HasValue)
            dict["GameTime"] = GameTimeMinutes.Value;

        foreach (var kv in Properties)
            dict[kv.Key] = kv.Value;

        return dict;
    }

    /// <inheritdoc/>
    public IDisposable BeginScope() => EngineLogContextAccessor.PushScope(this);
}
