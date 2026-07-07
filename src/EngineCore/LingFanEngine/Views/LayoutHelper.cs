using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// 布局辅助工具——统一解析像素/百分比，支持精确比例定位
/// <para>核心改进：百分比不再映射为 3 档对齐枚举，而是计算为精确像素值。</para>
/// <para>窗口缩放时，通过 RelayoutAllControls 重新调用 ApplyLayout 更新位置。</para>
/// </summary>
internal static class LayoutHelper
{
    // ========== 值解析 ==========

    /// <summary>
    /// 解析属性值——支持 double/int/float 和字符串("50%"/"640")
    /// </summary>
    public static double ParseDouble(Dictionary<string, object> props, string key)
    {
        if (!props.TryGetValue(key, out var val)) return key switch
        {
            "opacity" => 1.0,
            "scale" or "scaleX" or "scaleY" => 1.0,
            _ => 0
        };
        return val switch
        {
            double d => d,
            int i => i,
            float f => f,
            string s => double.TryParse(s.TrimEnd('%'),
                NumberStyles.Float,
                NumberFormatInfo.InvariantInfo, out var parsed) ? parsed : 0,
            _ => 0
        };
    }

    /// <summary>安全解析 int 属性</summary>
    public static int? ParseInt(Dictionary<string, object> props, string key)
    {
        if (!props.TryGetValue(key, out var val)) return null;
        return val switch
        {
            int i => i,
            double d => (int)d,
            string s => int.TryParse(s, out var parsed) ? parsed : null,
            _ => null
        };
    }

    /// <summary>
    /// 统一解析 size 值——支持数字(double/int/float)和字符串("50%"/"640")
    /// <para>返回解析后的像素值（百分比已乘以 parentSize）</para>
    /// </summary>
    public static double ResolveSize(Dictionary<string, object> props, string key, double parentSize)
    {
        if (!props.TryGetValue(key, out var val))
            return 0;
        if (val is double dv) return dv;
        if (val is int iv) return iv;
        if (val is float fv) return fv;
        if (val is string s)
        {
            s = s.Trim();
            if (s.EndsWith('%') && double.TryParse(s.AsSpan(0, s.Length - 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var pct))
                return parentSize * pct / 100.0;
            if (double.TryParse(s, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return 0;
    }

    /// <summary>
    /// 解析值并返回是否为百分比
    /// </summary>
    public static (double value, bool isPercent) ParseValueWithPercent(object? val)
    {
        if (val is double dv) return (dv, false);
        if (val is int iv) return (iv, false);
        if (val is float fv) return (fv, false);
        if (val is string s)
        {
            s = s.Trim();
            if (s.EndsWith('%') && double.TryParse(s.AsSpan(0, s.Length - 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var pct))
                return (pct, true);
            if (double.TryParse(s, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed))
                return (parsed, false);
        }
        return (0, false);
    }

    /// <summary>解析 "10,5,0,0" 或 "10" 格式的 Thickness</summary>
    public static Thickness ParseThickness(string s)
    {
        var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4 && double.TryParse(parts[0], out var l)
            && double.TryParse(parts[1], out var t)
            && double.TryParse(parts[2], out var r)
            && double.TryParse(parts[3], out var b))
            return new Thickness(l, t, r, b);
        if (parts.Length == 1 && double.TryParse(parts[0], out var all))
            return new Thickness(all);
        return new Thickness(0);
    }

    // ========== 容器类型判断 ==========

    /// <summary>判断元素类型是否为容器（可包含子元素）</summary>
    public static bool IsContainerType(string elementType)
    {
        var t = elementType.ToLowerInvariant();
        return t is "panel" or "frame" or "window" or "dialogbox" or "choicebox"
            or "infobox" or "overlay" or "popup" or "grid" or "stack"
            or "stackpanel" or "canvas" or "border" or "scroll" or "scrollviewer";
    }

    // ========== 百分比布局核心 ==========

    /// <summary>
    /// 计算百分比定位的 Margin
    /// <para>在 Grid/Panel 中，百分比 x/y 通过 Margin 实现精确定位。</para>
    /// <para>例如 x=25%, parentW=1280 → Margin.Left=320</para>
    /// </summary>
    public static double ResolvePercentPosition(object? val, double parentSize)
    {
        var (value, isPercent) = ParseValueWithPercent(val);
        return isPercent ? parentSize * value / 100.0 : value;
    }

    /// <summary>
    /// 解析尺寸值——百分比转为像素，固定值直接返回
    /// </summary>
    public static double ResolvePercentSize(object? val, double parentSize)
    {
        if (val == null) return double.NaN; // 未设置
        var (value, isPercent) = ParseValueWithPercent(val);
        return isPercent ? parentSize * value / 100.0 : value;
    }
}
