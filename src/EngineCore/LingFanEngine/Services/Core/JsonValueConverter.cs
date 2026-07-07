using System.Text.Json;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// JSON 值转换器实现
/// <para>将存档反序列化后的 JsonElement 值转换为 .NET 原生类型。</para>
/// <para>支持注册自定义转换器，优先级高于默认转换链。</para>
/// <para>从 GameLoop 静态方法迁移为实例方法，解除 StateContainer→GameLoop 循环依赖。</para>
/// </summary>
public class JsonValueConverter : IJsonValueConverter
{
    private readonly List<Func<JsonElement, object?>> _customConverters = [];

    private static readonly List<Func<JsonElement, object?>> s_defaultConverters =
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
        // 可选自扩展——开发者需要时自行 RegisterCustomConverter
    ];

    /// <inheritdoc/>
    public void RegisterCustomConverter(Func<JsonElement, object?> converter)
    {
        _customConverters.Insert(0, converter);
    }

    /// <inheritdoc/>
    public object? Convert(object? value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Number => _customConverters
                    .Concat(s_defaultConverters)
                    .Select(f => f(je))
                    .FirstOrDefault(r => r != null) ?? je.GetDecimal(),
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
}
