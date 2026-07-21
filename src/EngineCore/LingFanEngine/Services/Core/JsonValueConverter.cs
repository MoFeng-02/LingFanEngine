using System.Collections.Immutable;
using System.Globalization;
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
        // 注意：.NET 10 的 JsonElement.TryGetXxx 对非目标 ValueKind 会抛 InvalidOperationException，
        // 因此每个数字转换器必须先校验 ValueKind == Number，避免对非数字元素误抛异常。
        // Guid/DateTime 等字符串型转换器已从默认链移除：原实现仅 Number 分支走链，字符串从不自动转换，
        // 移除可避免“日期格式字符串被误转”的行为变更；需此类转换请通过 RegisterCustomConverter 显式注册。
        je => je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i) ? i : null,
        je => je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var l) ? l : null,
        je => je.ValueKind == JsonValueKind.Number && je.TryGetInt16(out var s) ? s : null,
        je => je.ValueKind == JsonValueKind.Number && je.TryGetByte(out var t) ? t : null,
        // 浮点数：double 优先于 float（JSON 数字默认精度）
        je => je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var d) ? d : null,
        je => je.ValueKind == JsonValueKind.Number && je.TryGetSingle(out var f) ? f : null,
        je => je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var d) ? d : null,
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
            // 统一走合并后的转换器链（自定义转换器在前），首个非 null 结果即采用。
            // 此前仅数字分支走链，字符串/数组/对象等绕过链——导致 RegisterCustomConverter
            // 对字符串等类型失效。现对任意类型均先尝试链，未命中再按 JsonElement 类型还原。
            var converters = _allConverters;
            foreach (var converter in converters)
            {
                var result = converter(je);
                if (result != null)
                    return result;
            }

            // 链未命中（默认转换器对该类型无匹配）：按原生类型还原
            return je.ValueKind switch
            {
                JsonValueKind.Number => je.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Null => null,
                JsonValueKind.Array => je.EnumerateArray().Select(je2 => Convert(je2)).ToList(),
                JsonValueKind.Object => RestoreTypedMarker(je),
                _ => value
            };
        }
        // E2 修复：内存对象图递归还原（DictStringObject 等内存 Dictionary 存档路径），
        // 识别 __lf_dt/__lf_guid 类型标注字典，还原嵌套 DateTime/Guid 为原生类型。
        // 与 JsonElement 路径的 RestoreTypedMarker 对称；DateTime.TryParse/Guid.TryParse 为 AOT 友好 API。
        if (value is System.Collections.IDictionary dict)
        {
            if (dict is Dictionary<string, object?> d)
            {
                if (d.TryGetValue("__lf_dt", out var dtv) && dtv is string dts
                    && DateTime.TryParse(dts, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    return dt;
                if (d.TryGetValue("__lf_guid", out var gv) && gv is string gvs && Guid.TryParse(gvs, out var g))
                    return g;
            }
            return dict.Keys.Cast<object?>().ToDictionary(k => k?.ToString() ?? "", k => Convert(dict[k]));
        }
        if (value is System.Collections.IEnumerable en and not string)
            return en.Cast<object?>().Select(Convert).ToList();
        return value;
    }

    /// <summary>
    /// E2 修复：识别嵌套 DateTime/Guid 类型标注包装（由 SaveDataService.NormalizeForSave 写入），
    /// 还原为原生类型，避免嵌套 DateTime/Guid 经 JSON 往返后退化成字符串（值保留但类型丢失）。
    /// <para>仅对含专用标注键的字典做还原，普通字典原样递归；DateTime.TryParse/Guid.TryParse 为 AOT 友好 API，无反射。</para>
    /// </summary>
    private object? RestoreTypedMarker(JsonElement je)
    {
        if (je.TryGetProperty("__lf_dt", out var dtEl) && dtEl.ValueKind == JsonValueKind.String
            && DateTime.TryParse(dtEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        if (je.TryGetProperty("__lf_guid", out var gEl) && gEl.ValueKind == JsonValueKind.String
            && Guid.TryParse(gEl.GetString(), out var g))
            return g;
        return je.EnumerateObject().ToDictionary(p => p.Name, p => Convert(p.Value));
    }
}
