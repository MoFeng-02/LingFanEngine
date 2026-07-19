using System.Collections.Immutable;
using System.Text.Json;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// JSON 值转换器实现
/// <para>将存档反序列化后的 JsonElement 值转换为 .NET 原生类型。</para>
/// <para>支持注册自定义转换器，优先级高于默认转换链。</para>
/// <para>从 GameLoop 静态方法迁移为实例方法，解除 StateContainer→GameLoop 循环依赖。</para>
/// <para>Phase 64：无锁设计——ImmutableArray + ImmutableInterlocked 原子替换。</para>
/// <para>F5-3 修复：去掉 _customConverters 字段，只维护 _allConverters，</para>
/// <para>单次 ImmutableInterlocked.Update 保证原子性（消除两次 Update 间的竞态窗口）。</para>
/// </summary>
public class JsonValueConverter : IJsonValueConverter
{
    /// <summary>
    /// 合并后的完整转换器链（自定义在前 + 默认在后）。
    /// 单次 ImmutableInterlocked.Update 原子替换，读取时直接遍历（不可变快照）。
    /// </summary>
    private ImmutableArray<Func<JsonElement, object?>> _allConverters = ImmutableArray<Func<JsonElement, object?>>.Empty;

    private static readonly ImmutableArray<Func<JsonElement, object?>> s_defaultConverters =
    [
        // 顺序很重要：int 最常见，优先于 short/byte
        je => je.TryGetInt32(out var i) ? i : null,
        je => je.TryGetInt64(out var l) ? l : null,
        je => je.TryGetInt16(out var s) ? s : null,
        je => je.TryGetByte(out var t) ? t : null,
        // 浮点数：double 优先于 float（JSON 数字默认精度）
        je => je.TryGetDouble(out var d) ? d : null,
        je => je.TryGetSingle(out var f) ? f : null,
        je => je.TryGetDecimal(out var d) ? d : null,
        // 非数值类型
        je => je.TryGetGuid(out var guid) ? guid : null,
        je => je.TryGetDateTimeOffset(out var t) ? t : null,
        je => je.TryGetDateTime(out var t) ? t : null,
    ];

    /// <summary>
    /// 静态构造——初始化 _allConverters 为默认转换器链
    /// </summary>
    public JsonValueConverter()
    {
        _allConverters = s_defaultConverters;
    }

    /// <inheritdoc/>
    public void RegisterCustomConverter(Func<JsonElement, object?> converter)
    {
        // 单次原子替换——自定义 converter 插到最前，再追加默认 converters
        // 消除两次 Update 间的竞态窗口（F5-3 修复）
        ImmutableInterlocked.Update(ref _allConverters,
            arr => ImmutableArray.Create(converter).AddRange(arr));
    }

    /// <inheritdoc/>
    public object? Convert(object? value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                // 直接遍历 _allConverters——ImmutableArray 不可变快照，无需加锁
                // 用 foreach 而非 LINQ（避免迭代器分配）
                JsonValueKind.Number => ConvertNumber(je),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Null => null,
                JsonValueKind.Array => je.EnumerateArray().Select(je2 => Convert(je2)).ToList(),
                JsonValueKind.Object => je.EnumerateObject()
                    .ToDictionary(p => p.Name, p => Convert(p.Value)),
                _ => value
            };
        }
        return value;
    }

    /// <summary>数字转换——遍历转换器链，第一个非 null 结果即返回</summary>
    private object? ConvertNumber(JsonElement je)
    {
        // 拍快照——原子读引用
        var converters = _allConverters;
        foreach (var converter in converters)
        {
            var result = converter(je);
            if (result != null)
                return result;
        }
        return je.GetDecimal();
    }
}
