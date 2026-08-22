using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Themes;

namespace LingFanEngine.SDK.Views.Controls;

/// <summary>
/// 公共 UI 构建器——统一间距体系与主题（ThemePalette），供各页面复用，消灭逐页重复的 Code-only 构建器。
/// <para>间距：M=16 页面边距 · S=8 控件间距 · XS=4 紧凑 · LabelW=100 表单标签列宽。</para>
/// </summary>
public static class UiBuilders
{
    /// <summary>页面边距</summary>
    public const double M = 16;

    /// <summary>控件间距</summary>
    public const double S = 8;

    /// <summary>紧凑间距</summary>
    public const double XS = 4;

    /// <summary>表单标签列宽</summary>
    public const double LabelW = 100;

    /// <summary>操作按钮（三形态：primary 主 / ghost 幽灵 / cancel 取消）</summary>
    public static Button ActionButton(string text, ICommand? cmd, bool isPrimary = false, bool isGhost = false, bool isCancel = false)
    {
        var btn = new Button
        {
            Content = text,
            Command = cmd,
            Padding = new Thickness(14, 6),
            FontSize = 12,
        };
        if (isPrimary)
        {
            btn.Background = ThemePalette.Accent;
            btn.Foreground = ThemePalette.TextBright;
            btn.BorderThickness = new Thickness(0);
        }
        else if (isGhost || isCancel)
        {
            btn.Classes.Add("transparent");
        }
        return btn;
    }

    /// <summary>卡片容器：标题 + 内容 + 可选底部说明</summary>
    public static Control Card(string title, Control body, Control? footer)
    {
        var content = new StackPanel { Spacing = S, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12, FontWeight = FontWeight.Bold,
            Foreground = ThemePalette.Title,
        });
        content.Children.Add(body);
        if (footer != null)
            content.Children.Add(footer);

        return new Border
        {
            Background = ThemePalette.CardBg,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(M, S + 4, M, S + 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content,
        };
    }

    /// <summary>
    /// 表单网格（单列 N 行，每行一个 <see cref="FormRow"/> 占满整行）。
    /// <para>必须 <c>*</c> 单列，让 FormRow 拿到父容器全宽；双列会让其内 <c>{LabelW},*</c> 的 * 宽度=0 → 输入被压扁。</para>
    /// </summary>
    public static Grid FormGrid(params Control[] rows)
    {
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*"),
            RowDefinitions = RowDefinitions.Parse(string.Join(",", Enumerable.Repeat("Auto", rows.Length))),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        for (var i = 0; i < rows.Length; i++)
        {
            Grid.SetRow(rows[i], i);
            grid.Children.Add(rows[i]);
        }
        return grid;
    }

    /// <summary>表单行：标签（0 列）+ 输入控件（1 列，星号占满）</summary>
    public static Control FormRow(string label, Control input)
    {
        Grid.SetColumn(input, 1);
        input.Margin = new Thickness(S, 2, 0, 2);
        input.VerticalAlignment = VerticalAlignment.Center;
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (input is TextBox) input.MinWidth = 220;
        if (input is ComboBox) input.MinWidth = 200;
        if (input is NumericUpDown) input.MinWidth = 120;

        var labelBlock = new TextBlock
        {
            Text = label + ":", Foreground = ThemePalette.TextMuted, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, S, 0),
        };

        return new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse($"{LabelW},*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { labelBlock, input },
        };
    }

    /// <summary>带标签的下拉框（工具栏横向项）</summary>
    public static Control LabeledCombo(string label, string[] items, int selected, Action<int> onSelect, double width = 140)
    {
        var wrap = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Margin = new Thickness(M, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        wrap.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = ThemePalette.TextMuted });
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = selected, FontSize = 12, Width = width };
        combo.SelectionChanged += (_, _) => onSelect(combo.SelectedIndex);
        wrap.Children.Add(combo);
        return wrap;
    }

    /// <summary>带标签的文本框（工具栏横向项）</summary>
    public static Control LabeledTextBox(string label, string initial, Action<string> onChanged, double width, string? watermark = null)
    {
        var wrap = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Margin = new Thickness(M, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        wrap.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = ThemePalette.TextMuted });
        var box = new TextBox { Text = initial, FontSize = 12, Width = width, PlaceholderText = watermark };
        box.TextChanged += (_, _) => onChanged(box.Text ?? initial);
        wrap.Children.Add(box);
        return wrap;
    }

    /// <summary>表单文本框（输入列星号拉伸）</summary>
    public static TextBox FormTextBox(string initial, Action<string> setter, bool isPassword = false)
    {
        var tb = new TextBox
        {
            Text = initial,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (isPassword) tb.PasswordChar = '*';
        tb.TextChanged += (_, _) => setter(tb.Text ?? "");
        return tb;
    }

    /// <summary>表单数字框（输入列星号拉伸）</summary>
    public static NumericUpDown FormNumericBox(int initial, Action<int> setter, int min, int max)
    {
        var box = new NumericUpDown
        {
            Value = initial, Minimum = min, Maximum = max,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 200,
        };
        box.ValueChanged += (_, _) => setter((int)box.Value);
        return box;
    }

    /// <summary>状态徽章（Success 绿 / Warn 橙，列表行状态）</summary>
    public static Border StatusBadge(string text, bool isPositive)
    {
        var brush = isPositive ? ThemePalette.Success : ThemePalette.Warn;
        var tint = new SolidColorBrush(isPositive
            ? Color.FromArgb(30, 78, 201, 176)
            : Color.FromArgb(30, 230, 165, 60));
        return new Border
        {
            Background = tint,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = brush,
            },
        };
    }
}