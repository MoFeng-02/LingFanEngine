using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LingFanEngine.SDK.Themes;

/// <summary>
/// 统一主题访问器——从 <c>Themes/Colors.axaml</c> 令牌（Brush.*）解析为静态画笔，作为 C# code-behind 控件的单一主题源。
/// <para>与 XAML 的 <c>{DynamicResource Brush.*}</c> 共读同一 <see cref="Application.Resources"/>，保证全应用配色一致。</para>
/// <para>命名避开 <see cref="StyledElement.Theme"/> 成员。</para>
/// </summary>
public static class ThemePalette
{
    /// <summary>编辑区背景 #1E1E1E</summary>
    public static IBrush EditorBg => Resolve("Brush.EditorBg");
    /// <summary>编辑区更深背景（预览文本区） #1B1B1B</summary>
    public static IBrush EditorBgDark => Resolve("Brush.EditorBgDark");
    /// <summary>侧栏/面板背景 #252526</summary>
    public static IBrush PanelBg => Resolve("Brush.PanelBg");
    /// <summary>输入区背景 #2D2D30</summary>
    public static IBrush PanelDeeper => Resolve("Brush.PanelDeeper");
    /// <summary>活动栏背景 #333333</summary>
    public static IBrush ActivityBarBg => Resolve("Brush.ActivityBarBg");
    /// <summary>状态栏背景 #007ACC</summary>
    public static IBrush StatusBarBg => Resolve("Brush.StatusBarBg");
    /// <summary>边框 #3C3C3C</summary>
    public static IBrush Border => Resolve("Brush.Border");
    /// <summary>分隔线 #1E1E1E</summary>
    public static IBrush Splitter => Resolve("Brush.Splitter");
    /// <summary>输入框边框 #3F3F46</summary>
    public static IBrush InputBorder => Resolve("Brush.InputBorder");
    /// <summary>正文文本 #D4D4D4</summary>
    public static IBrush Text => Resolve("Brush.Text");
    /// <summary>次级文本 #8A8A8A</summary>
    public static IBrush TextMuted => Resolve("Brush.TextMuted");
    /// <summary>高亮文本 #FFFFFF</summary>
    public static IBrush TextBright => Resolve("Brush.TextBright");
    /// <summary>悬停文本 #CCCCCC</summary>
    public static IBrush TextHover => Resolve("Brush.TextHover");
    /// <summary>图标常态 #858585</summary>
    public static IBrush IconNormal => Resolve("Brush.IconNormal");
    /// <summary>图标激活 #FFFFFF</summary>
    public static IBrush IconActive => Resolve("Brush.IconActive");
    /// <summary>主操作色 #0E639C</summary>
    public static IBrush Accent => Resolve("Brush.Accent");
    /// <summary>成功 #4EC9B0</summary>
    public static IBrush Success => Resolve("Brush.Success");
    /// <summary>警告/待审批 #CE9178</summary>
    public static IBrush Warn => Resolve("Brush.Warn");
    /// <summary>标题强调 #4FC1FF</summary>
    public static IBrush Title => Resolve("Brush.Title");
    /// <summary>卡片半透明白 #0FFFFFFF</summary>
    public static IBrush CardBg => Resolve("Brush.CardBg");
    /// <summary>运行（启动游戏） #2D9D78</summary>
    public static IBrush RunGreen => Resolve("Brush.RunGreen");
    /// <summary>停止（停止游戏） #C0392B</summary>
    public static IBrush StopRed => Resolve("Brush.StopRed");
    /// <summary>信息性文案 #88C0D0</summary>
    public static IBrush Info => Resolve("Brush.Info");
    /// <summary>启动器按钮底 #3A3D41</summary>
    public static IBrush BtnBg => Resolve("Brush.BtnBg");

    private static IBrush Resolve(string key)
    {
        if (Application.Current is { } app
            && app.TryFindResource(key, app.ActualThemeVariant, out var value)
            && value is IBrush brush)
            return brush;
        return Brushes.Gray; // 资源未加载/单测环境回退，避免 null
    }
}