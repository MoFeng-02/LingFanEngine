using System.Collections.Generic;
using LingFanEngine.Abstractions.Entities.UIs;

namespace LingFanEngine.Entry;

/// <summary>
/// Demo Scene 构建辅助方法（Txt/Btn/Img 工厂）
/// <para>x/y 和 halign/valign 组合使用：halign=center 时 x 为偏移量，valign=center 时 y 为偏移量</para>
/// </summary>
public static class DemoHelpers
{
    /// <summary>创建文本元素</summary>
    public static UIElementEntity Txt(string text, double x, double y,
        double fontSize = 16, string color = "#FFFFFF",
        string halign = "left", string valign = "top",
        string font = "Microsoft YaHei", int order = 0)
    {
        return new UIElementEntity
        {
            ElementType = "text",
            Properties = new Dictionary<string, object>
            {
                ["text"] = text, ["x"] = x, ["y"] = y,
                ["fontSize"] = fontSize, ["color"] = color,
                ["halign"] = halign, ["valign"] = valign,
                ["font"] = font
            },
            Order = order
        };
    }

    /// <summary>创建按钮元素</summary>
    public static UIElementEntity Btn(string text, double x, double y,
        double w, double h, string? nav = null, string? cmd = null,
        string color = "#88CCFF", string? hoverColor = null,
        string halign = "left", string valign = "top", int order = 0)
    {
        var props = new Dictionary<string, object>
        {
            ["text"] = text, ["x"] = x, ["y"] = y,
            ["width"] = w, ["height"] = h, ["color"] = color,
            ["halign"] = halign, ["valign"] = valign
        };
        if (nav != null) props["nav"] = nav;
        if (cmd != null) props["cmd"] = cmd;
        if (hoverColor != null) props["hover_color"] = hoverColor;
        return new UIElementEntity
        {
            ElementType = "button", Properties = props, Order = order,
            Command = cmd ?? nav, CommandValue = null
        };
    }

    /// <summary>创建图片元素</summary>
    public static UIElementEntity Img(string source, double x, double y,
        object? w = null, object? h = null, double opacity = 1.0,
        string halign = "left", string valign = "top",
        string elementType = "image", int order = 0)
    {
        var props = new Dictionary<string, object>
        {
            ["source"] = source, ["x"] = x, ["y"] = y,
            ["halign"] = halign, ["valign"] = valign
        };
        if (w != null) props["width"] = w;
        if (h != null) props["height"] = h;
        if (opacity < 1.0) props["opacity"] = opacity;
        return new UIElementEntity { ElementType = elementType, Properties = props, Order = order };
    }
}