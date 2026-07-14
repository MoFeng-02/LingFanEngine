using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 设置页面
/// <para>编辑器设置/主题/构建/SDK 信息分组。</para>
/// </summary>
public class SettingsPage : UserControl, INavigationAware
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent(viewModel);
    }

    // ===== INavigationAware =====

    public void OnNavigated(Dictionary<string, object?>? parameters) { }
    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    private void InitializeComponent(SettingsViewModel viewModel)
    {
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var grid = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,Auto,Auto,*"),
            Margin = new Thickness(16),
        };

        // 标题
        grid.Children.Add(new TextBlock
        {
            Text = "设置",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 16),
        });

        // === 编辑器设置 ===
        var editorPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
        Grid.SetRow(editorPanel, 1);

        editorPanel.Children.Add(new TextBlock
        {
            Text = "编辑器",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#4FC1FF")),
        });

        editorPanel.Children.Add(CreateSettingRow("字体", CreateTextBox(
            () => viewModel.EditorFontFamily, v => viewModel.EditorFontFamily = v)));

        var fontSizeBox = new NumericUpDown
        {
            Value = viewModel.EditorFontSize,
            Minimum = 8,
            Maximum = 32,
        };
        fontSizeBox.ValueChanged += (_, _) => viewModel.EditorFontSize = (int)fontSizeBox.Value;
        editorPanel.Children.Add(CreateSettingRow("字号", fontSizeBox));

        var indentStyleCombo = new ComboBox { MinWidth = 120 };
        indentStyleCombo.Items.Add("空格");
        indentStyleCombo.Items.Add("Tab");
        indentStyleCombo.SelectedIndex = viewModel.IndentStyle == "spaces" ? 0 : 1;
        indentStyleCombo.SelectionChanged += (_, _) =>
            viewModel.IndentStyle = indentStyleCombo.SelectedIndex == 0 ? "spaces" : "tabs";
        editorPanel.Children.Add(CreateSettingRow("缩进风格", indentStyleCombo));

        var indentWidthBox = new NumericUpDown
        {
            Value = viewModel.IndentWidth,
            Minimum = 2,
            Maximum = 8,
        };
        indentWidthBox.ValueChanged += (_, _) => viewModel.IndentWidth = (int)indentWidthBox.Value;
        editorPanel.Children.Add(CreateSettingRow("缩进宽度", indentWidthBox));

        editorPanel.Children.Add(CreateSettingRow("保存时格式化", CreateCheckBox(
            () => viewModel.FormatOnSave, v => viewModel.FormatOnSave = v)));

        editorPanel.Children.Add(CreateSettingRow("显示行号", CreateCheckBox(
            () => viewModel.ShowLineNumbers, v => viewModel.ShowLineNumbers = v)));

        editorPanel.Children.Add(CreateSettingRow("显示 Minimap", CreateCheckBox(
            () => viewModel.ShowMinimap, v => viewModel.ShowMinimap = v)));

        editorPanel.Children.Add(CreateSettingRow("自动换行", CreateCheckBox(
            () => viewModel.WordWrap, v => viewModel.WordWrap = v)));

        grid.Children.Add(editorPanel);

        // === 构建设置 ===
        var buildPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
        Grid.SetRow(buildPanel, 2);

        buildPanel.Children.Add(new TextBlock
        {
            Text = "构建",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#4FC1FF")),
        });

        var buildConfigCombo = new ComboBox { MinWidth = 120 };
        buildConfigCombo.Items.Add("Debug");
        buildConfigCombo.Items.Add("Release");
        buildConfigCombo.SelectedIndex = viewModel.DefaultBuildConfig == "Debug" ? 0 : 1;
        buildConfigCombo.SelectionChanged += (_, _) =>
            viewModel.DefaultBuildConfig = buildConfigCombo.SelectedIndex == 0 ? "Debug" : "Release";
        buildPanel.Children.Add(CreateSettingRow("默认配置", buildConfigCombo));

        buildPanel.Children.Add(CreateSettingRow("默认自包含", CreateCheckBox(
            () => viewModel.DefaultSelfContained, v => viewModel.DefaultSelfContained = v)));

        buildPanel.Children.Add(CreateSettingRow("默认 AOT", CreateCheckBox(
            () => viewModel.DefaultPublishAot, v => viewModel.DefaultPublishAot = v)));

        grid.Children.Add(buildPanel);

        // === SDK 信息 ===
        var sdkPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
        Grid.SetRow(sdkPanel, 3);

        sdkPanel.Children.Add(new TextBlock
        {
            Text = "SDK 信息",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#4FC1FF")),
        });

        sdkPanel.Children.Add(CreateInfoRow("SDK 版本", viewModel.SdkVersion));
        sdkPanel.Children.Add(CreateInfoRow("引擎版本", viewModel.EngineVersion));
        sdkPanel.Children.Add(CreateInfoRow(".NET 版本", viewModel.DotNetVersion));
        sdkPanel.Children.Add(CreateInfoRow("应用数据目录", viewModel.AppDataDirectory));

        var openDataBtn = new Button
        {
            Content = "打开数据目录",
            Command = viewModel.OpenAppDataCommand,
            Margin = new Thickness(0, 4, 0, 0),
        };
        sdkPanel.Children.Add(openDataBtn);

        grid.Children.Add(sdkPanel);

        // === 保存按钮 + 状态 ===
        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 16),
        };
        Grid.SetRow(actionPanel, 4);

        var saveBtn = new Button
        {
            Content = "保存设置",
            Command = viewModel.SaveSettingsCommand,
            Background = new SolidColorBrush(Color.Parse("#0E639C")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 6),
        };
        actionPanel.Children.Add(saveBtn);

        var statusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = Brushes.Gray,
        };
        statusText.Text = viewModel.StatusMessage;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.StatusMessage))
                statusText.Text = viewModel.StatusMessage;
        };
        actionPanel.Children.Add(statusText);

        grid.Children.Add(actionPanel);

        scrollViewer.Content = grid;
        Content = scrollViewer;
    }

    private static StackPanel CreateSettingRow(string label, Control control)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 2),
            Children =
            {
                new TextBlock
                {
                    Text = label + ":",
                    Width = 120,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                control,
            }
        };
    }

    private static TextBox CreateTextBox(Func<string> getValue, Action<string> setValue)
    {
        var tb = new TextBox { MinWidth = 200, Text = getValue() };
        tb.TextChanged += (_, _) => setValue(tb.Text);
        return tb;
    }

    private static CheckBox CreateCheckBox(Func<bool> getValue, Action<bool> setValue)
    {
        var cb = new CheckBox { IsChecked = getValue() };
        cb.IsCheckedChanged += (_, _) => setValue(cb.IsChecked ?? false);
        return cb;
    }

    private static StackPanel CreateInfoRow(string label, string value)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = label + ":",
                    FontWeight = FontWeight.Bold,
                    Width = 120,
                },
                new TextBlock { Text = value },
            }
        };
    }
}
