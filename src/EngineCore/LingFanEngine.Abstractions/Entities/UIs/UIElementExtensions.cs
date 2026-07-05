using System.Collections.Generic;

namespace LingFanEngine.Abstractions.Entities.UIs;

/// <summary>
/// UIElementEntity 扩展方法——用 Fluent 语法设 Properties 字典
/// <para>底层仍是 List + 字典驱动，这些方法只是语法糖。</para>
/// <para>DSL 解析器直接生成 Dictionary，无需此扩展也能工作。</para>
/// </summary>
public static class UIElementExtensions
{
    // ========== 坐标布局 ==========

    public static UIElementEntity At(this UIElementEntity e, string x, string y)
    {
        e.Properties["x"] = x;
        e.Properties["y"] = y;
        return e;
    }

    public static UIElementEntity Size(this UIElementEntity e, string w, string h)
    {
        e.Properties["width"] = w;
        e.Properties["height"] = h;
        return e;
    }

    public static UIElementEntity Size(this UIElementEntity e, double w, double h)
        => e.Size(w.ToString(), h.ToString());

    public static UIElementEntity Margin(this UIElementEntity e, string margin)
    {
        e.Properties["margin"] = margin;
        return e;
    }

    public static UIElementEntity Padding(this UIElementEntity e, string padding)
    {
        e.Properties["padding"] = padding;
        return e;
    }

    // ========== 文字 ==========

    public static UIElementEntity FontSize(this UIElementEntity e, string size)
    {
        e.Properties["fontSize"] = size;
        return e;
    }
    public static UIElementEntity FontSize(this UIElementEntity e, double size)
        => e.FontSize(size.ToString());

    public static UIElementEntity Color(this UIElementEntity e, string color)
    {
        e.Properties["color"] = color;
        return e;
    }

    public static UIElementEntity Center(this UIElementEntity e)
    {
        e.Properties["textAlign"] = "center";
        return e;
    }

    public static UIElementEntity Font(this UIElementEntity e, string font)
    {
        e.Properties["fontFamily"] = font;
        return e;
    }

    public static UIElementEntity MaxWidth(this UIElementEntity e, string max)
    {
        e.Properties["maxWidth"] = max;
        return e;
    }
    public static UIElementEntity MaxWidth(this UIElementEntity e, double max)
        => e.MaxWidth(max.ToString());

    // ========== 按钮 ==========

    public static UIElementEntity Nav(this UIElementEntity e, string nav)
    {
        e.Properties["nav"] = nav;
        e.Command = nav;
        return e;
    }

    public static UIElementEntity Cmd(this UIElementEntity e, string cmd, string? value = null)
    {
        e.Properties["cmd"] = cmd;
        e.Command = cmd;
        if (value != null)
        {
            e.Properties["value"] = value;
            e.CommandValue = value;
        }
        return e;
    }

    // ========== 图片 ==========

    public static UIElementEntity Opacity(this UIElementEntity e, string opacity)
    {
        e.Properties["opacity"] = opacity;
        return e;
    }
    public static UIElementEntity Opacity(this UIElementEntity e, double opacity)
        => e.Opacity(opacity.ToString("0.##"));

    // ========== 通用 ==========

    public static UIElementEntity Order(this UIElementEntity e, int order)
    {
        e.Order = order;
        return e;
    }
}
