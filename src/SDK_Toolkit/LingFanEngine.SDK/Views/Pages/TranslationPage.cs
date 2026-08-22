using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.I18n;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Themes;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views.Controls;
using MFToolkit.Routing;
using MFToolkit.Routing.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 多语言翻译页面。
/// <para>布局设计（Avalonia 规则，纯 C# Code-only，统一间距体系）：</para>
/// <para>  M = 16（页面边距） · S = 8（控件间距） · XS = 4（紧凑间距）</para>
/// <para>骨架（Grid 严格分栏）：</para>
/// <para>  ① 工具栏（Auto）：标题区 + 语言 + 模式 + 操作按钮，DockPanel 右对齐</para>
/// <para>  ② 配置区（Auto）：模式相关卡片（AI/API），Grid 两列表单——标签列 100px + 输入列星号弹性</para>
/// <para>  ③ 进度条（Auto）④ 统计行（Auto）⑤ 结果列表（* 弹性占满）⑥ 状态栏（Auto）</para>
/// </summary>
public class TranslationPage : UserControl, INavigationAware
{
    private readonly TranslationViewModel _vm;
    private bool _vmEventsWired;
    private bool _cultureSubscribed;

    // ===== 布局常量（统一间距体系）=====
    private const double M = 16;   // 页面边距
    private const double S = 8;    // 控件间距
    private const double XS = 4;   // 紧凑间距
    private const double LabelW = 100; // 表单标签列宽

    // ===== 控件引用（事件订阅用）=====
    private StackPanel _aiConfigPanel = null!;
    private StackPanel _apiConfigPanel = null!;
    private ComboBox _modelCombo = null!;
    private ListBox _entriesList = null!;
    private TextBlock _statusText = null!;
    private TextBlock _statText = null!;
    private ProgressBar _progressBar = null!;
    private Button _syncButton = null!;
    private Button _cancelButton = null!;
    private Control _previewPanel = null!;
    private TextBox _previewText = null!;

    // ===== 统一色板：取自 Themes/Colors.axaml（C# 经 ThemePalette.* 读取，XAML 经 {DynamicResource Brush.*}）=====
    // 不再在页内硬编码 s_* 画笔——见 LingFanEngine.SDK.Themes.Theme。

    public TranslationPage(TranslationViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        if (!_cultureSubscribed)
        {
            _cultureSubscribed = true;
            // 语言切换 → 整页受控重建（重读所有 SdkLocalizer.Loc 静态文案）
            SdkLocalizer.CultureChanged += RebuildForCulture;
        }
        InitializeComponent();
    }

    /// <summary>语言切换时重建页面内容；VM 持久事件已单次接线，重复构建不会叠加订阅。</summary>
    private void RebuildForCulture() => InitializeComponent();

    public void OnNavigated(Dictionary<string, object?>? parameters)
    {
        // 从「模型管理」返回时刷新模型列表与选中态
        _vm.RefreshModels();
    }
    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    private void InitializeComponent()
    {
        // ===== 骨架：7 行严格分栏（列表 * 弹性占满）=====
        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,Auto,Auto,*,Auto"),
            Background = ThemePalette.EditorBg,
        };

        root.Children.Add(CreateToolbar());
        Grid.SetRow(root.Children[^1], 0);

        var configArea = CreateConfigArea();
        root.Children.Add(configArea);
        Grid.SetRow(configArea, 1);

        _progressBar = new ProgressBar
        {
            Height = 4, Minimum = 0, Maximum = 100, Value = 0,
            IsVisible = false, Margin = new Thickness(M, XS, M, 0),
        };
        root.Children.Add(_progressBar);
        Grid.SetRow(_progressBar, 2);

        var statRow = CreateStatRow();
        root.Children.Add(statRow);
        Grid.SetRow(statRow, 3);

        _previewPanel = CreatePreviewPanel();
        root.Children.Add(_previewPanel);
        Grid.SetRow(_previewPanel, 4);

        _entriesList = new ListBox
        {
            Margin = new Thickness(M, S, M, S),
            Background = ThemePalette.PanelBg,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ItemsSource = _vm.Entries,
            Padding = new Thickness(S, S),
        };
        _entriesList.ItemTemplate = new FuncDataTemplate<TranslationEntry>((entry, _) => CreateEntryRow(entry), true);
        root.Children.Add(_entriesList);
        Grid.SetRow(_entriesList, 5);

        _statusText = new TextBlock
        {
            Text = _vm.StatusMessage, TextWrapping = TextWrapping.Wrap,
            Foreground = ThemePalette.TextMuted, FontSize = 12, Margin = new Thickness(M, 0, M, S),
        };
        root.Children.Add(_statusText);
        Grid.SetRow(_statusText, 6);

        WireVmEvents();

        Content = root;
    }

    /// <summary>
    /// VM 持久事件仅接线一次：语言切换重建时字段会被重新指向新控件，
    /// 已注册的处理器经字段读取最新控件引用，故重建不会叠加订阅。
    /// </summary>
    private void WireVmEvents()
    {
        if (_vmEventsWired) return;
        _vmEventsWired = true;

        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(TranslationViewModel.StatusMessage):
                    _statusText.Text = _vm.StatusMessage;
                    break;
                case nameof(TranslationViewModel.Progress):
                    _progressBar.Value = _vm.Progress;
                    break;
                case nameof(TranslationViewModel.IsBusy):
                    _progressBar.IsVisible = _vm.IsBusy;
                    _syncButton.IsEnabled = !_vm.IsBusy;
                    _cancelButton.IsVisible = _vm.IsBusy;
                    break;
                case nameof(TranslationViewModel.Mode):
                    _aiConfigPanel.IsVisible = _vm.Mode is TranslationMode.Ai or TranslationMode.Agent;
                    _apiConfigPanel.IsVisible = _vm.Mode == TranslationMode.Api;
                    break;
                case nameof(TranslationViewModel.HasPending):
                    _previewPanel.IsVisible = _vm.HasPending;
                    if (_vm.HasPending)
                        _previewText.Text = _vm.PreviewText;
                    break;
                case nameof(TranslationViewModel.PreviewText):
                    _previewText.Text = _vm.PreviewText;
                    break;
                case nameof(TranslationViewModel.ScannedCount):
                case nameof(TranslationViewModel.TranslatedCount):
                case nameof(TranslationViewModel.UntranslatedCount):
                    _statText.Text = SdkLocalizer.Loc("Tr_Stat", _vm.ScannedCount, _vm.TranslatedCount, _vm.UntranslatedCount);
                    break;
            }
        };

        // 模型列表变化（如从管理页返回）或选中切换后重新同步下拉选中项
        _vm.Models.CollectionChanged += (_, _) => SyncModelComboSelection();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TranslationViewModel.SelectedModelId))
                SyncModelComboSelection();
        };
    }

    // ============================================================
    // ① 工具栏：DockPanel 右对齐操作区，标题左侧
    // ============================================================

    private Control CreateToolbar()
    {
        // 工具栏拆成两行，避免单个 DockPanel 横向堆叠 6 个子项时互相覆盖：
        //   Row 1 — 标题（左，LastChildFill） + 目标语言 + 源语言（均 Dock.Right）
        //   Row 2 — 占位（左，LastChildFill） + 强制重翻译 + 翻译模式 + 操作按钮（均 Dock.Right）

        // ---------- 行 1 ----------
        var row1 = new DockPanel { LastChildFill = true };

        // 输出布局（Flat/Mirrored/SingleFile；切换即持久化到项目 ProjectConfig.TranslationLayout）
        var layoutField = UiBuilders.LabeledCombo(SdkLocalizer.Loc("Tr_Layout"),
            [SdkLocalizer.Loc("Layout_Flat"), SdkLocalizer.Loc("Layout_Mirrored"), SdkLocalizer.Loc("Layout_Single")],
            LayoutToIndex(_vm.Layout),
            v => _vm.Layout = v switch
            {
                1 => TranslationLayout.Mirrored,
                2 => TranslationLayout.SingleFile,
                _ => TranslationLayout.Flat,
            });
        layoutField.Margin = new Thickness(0, 0, M, 0);
        DockPanel.SetDock(layoutField, Dock.Right);
        row1.Children.Add(layoutField);

        var sourceField = UiBuilders.LabeledTextBox(SdkLocalizer.Loc("Tr_SourceLang"), _vm.SourceLang, v => _vm.SourceLang = v, width: 80, watermark: SdkLocalizer.Loc("Wm_AutoDetect"));
        sourceField.Margin = new Thickness(0, 0, M, 0);
        DockPanel.SetDock(sourceField, Dock.Right);
        row1.Children.Add(sourceField);

        // 目标语言（locale code：en-US / ja-JP / ko-KR…，遵循引擎 i18n 标准，作 Lang/{lang}/ 语言根目录名）
        var targetField = LabeledLocaleCombo(_vm.TargetLang, v => _vm.TargetLang = v);
        targetField.Margin = new Thickness(0, 0, M, 0);
        DockPanel.SetDock(targetField, Dock.Right);
        row1.Children.Add(targetField);

        // 标题（占满剩余，左对齐垂直居中）；副标题置于 * 列受宽度约束，超长省略号，
        // 避免长译文左移/叠到右侧目标语言下拉框下
        var titleText = new TextBlock { Text = SdkLocalizer.Loc("Tr_Title"), FontSize = 20, FontWeight = FontWeight.Bold, Foreground = ThemePalette.Text };
        var subText = new TextBlock
        {
            Text = SdkLocalizer.Loc("Tr_Sub"),
            FontSize = 11, Foreground = ThemePalette.TextMuted,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        var titleGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*") };
        titleGrid.Children.Add(titleText);
        Grid.SetColumn(titleText, 0);
        titleGrid.Children.Add(subText);
        Grid.SetColumn(subText, 1);
        row1.Children.Add(titleGrid);

        // ---------- 行 2 ----------
        var row2 = new DockPanel { LastChildFill = true };

        // 操作按钮组（最右）
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = S,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, M, 0),
        };
        _syncButton = UiBuilders.ActionButton(SdkLocalizer.Loc("Action_Sync"), _vm.SyncCommand, isPrimary: true);
        _cancelButton = UiBuilders.ActionButton(SdkLocalizer.Loc("Action_Cancel"), _vm.CancelSyncCommand, isCancel: true);
        _cancelButton.IsVisible = false;
        actions.Children.Add(UiBuilders.ActionButton(SdkLocalizer.Loc("Action_ScanText"), _vm.ScanCommand, isGhost: true));
        var genBtn = UiBuilders.ActionButton(SdkLocalizer.Loc("Action_Generate"), _vm.GenerateModeFilesCommand, isGhost: true);
        // 引导：生成的是可交给外部 AI 批量翻译的占位框架（ToolTip 提示）
        ToolTip.SetTip(genBtn, SdkLocalizer.Loc("Tip_GenerateFrame"));
        actions.Children.Add(genBtn);
        actions.Children.Add(_syncButton);
        actions.Children.Add(_cancelButton);
        DockPanel.SetDock(actions, Dock.Right);
        row2.Children.Add(actions);

        // 模式选择
        var modeField = UiBuilders.LabeledCombo(SdkLocalizer.Loc("Tr_Mode"), [SdkLocalizer.Loc("Mode_Manual"), SdkLocalizer.Loc("Mode_Ai"), SdkLocalizer.Loc("Mode_Api"), SdkLocalizer.Loc("Mode_Agent")], 0, v =>
        {
            _vm.Mode = v switch
            {
                1 => TranslationMode.Ai,
                2 => TranslationMode.Api,
                3 => TranslationMode.Agent,
                _ => TranslationMode.Manual,
            };
        });
        modeField.Margin = new Thickness(0, 0, M, 0);
        DockPanel.SetDock(modeField, Dock.Right);
        row2.Children.Add(modeField);

        // 强制重翻译（不勾=跳过已翻译，勾=全部重翻）
        var forceCheck = new CheckBox
        {
            Content = SdkLocalizer.Loc("Force_Retranslate"),
            IsChecked = _vm.ForceRetranslate,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = ThemePalette.TextMuted,
            Margin = new Thickness(0, 0, M, 0),
        };
        ToolTip.SetTip(forceCheck, SdkLocalizer.Loc("Tip_ForceRetranslate"));
        forceCheck.IsCheckedChanged += (_, _) => _vm.ForceRetranslate = forceCheck.IsChecked ?? false;
        DockPanel.SetDock(forceCheck, Dock.Right);
        row2.Children.Add(forceCheck);

        // 占位占满剩余（透明 StackPanel，避免右侧 Dock 元素向左挤压）
        row2.Children.Add(new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });

        var container = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = XS,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        container.Children.Add(row1);
        container.Children.Add(row2);

        return new Border
        {
            Background = ThemePalette.PanelBg,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(M, 10, M, 10),
            Child = container,
        };
    }

    // ============================================================
    // ② 配置区：AI/API 卡片（Grid 两列表单，标签列 100px + 输入列星号）
    // ============================================================

    private Control CreateConfigArea()
    {
        var container = new StackPanel
        {
            Margin = new Thickness(M, S, M, 0),
            Spacing = S,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // ---- AI 卡片 ----
        _aiConfigPanel = new StackPanel
        {
            Spacing = S,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                UiBuilders.Card(
                    SdkLocalizer.Loc("Tr_AiSection"),
                    UiBuilders.FormGrid(
                        UiBuilders.FormRow(SdkLocalizer.Loc("Form_Model"), CreateModelCombo())),
                    CreateAiCardFooter()),
            }
        };

        // ---- API 卡片 ----
        _apiConfigPanel = new StackPanel
        {
            Spacing = S,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                UiBuilders.Card(
                    SdkLocalizer.Loc("Tr_ApiSection"),
                    UiBuilders.FormGrid(
                        UiBuilders.FormRow(SdkLocalizer.Loc("Form_Endpoint"), UiBuilders.FormTextBox(_vm.ApiEndpoint, v => _vm.ApiEndpoint = v)),
                        UiBuilders.FormRow("API Key", UiBuilders.FormTextBox(_vm.ApiKey, v => _vm.ApiKey = v, isPassword: true)),
                        UiBuilders.FormRow(SdkLocalizer.Loc("Form_ApiTarget"), UiBuilders.FormTextBox(_vm.ApiTargetLangCode, v => _vm.ApiTargetLangCode = v))),
                    null),
            }
        };

        container.Children.Add(_aiConfigPanel);
        container.Children.Add(_apiConfigPanel);
        return container;
    }

    /// <summary>模型下拉（绑定 ViewModel.Models，选中写入 SelectedModelId）</summary>
    private Control CreateModelCombo()
    {
        _modelCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 220,
            ItemsSource = _vm.Models,
            ItemTemplate = new FuncDataTemplate<ModelConfig>((m, _) =>
                new TextBlock { Text = m?.DisplayOrId ?? "", FontSize = 12 }),
            PlaceholderText = SdkLocalizer.Loc("Ph_SelectModel"),
        };
        SyncModelComboSelection();
        _modelCombo.SelectionChanged += (_, _) =>
        {
            if (_modelCombo.SelectedItem is ModelConfig m)
                _vm.SelectedModelId = m.Id;
        };
        return _modelCombo;
    }

    private void SyncModelComboSelection()
    {
        var sel = _vm.Models.FirstOrDefault(m => m.Id == _vm.SelectedModelId);
        if (sel != null && !ReferenceEquals(_modelCombo.SelectedItem, sel))
            _modelCombo.SelectedItem = sel;
    }

    /// <summary>AI 卡片底部：管理模型按钮 + 提示</summary>
    private Control CreateAiCardFooter()
    {
        var panel = new StackPanel { Spacing = S };

        var btn = new Button
        {
            Content = SdkLocalizer.Loc("Action_ManageModels"),
            Padding = new Thickness(14, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = ThemePalette.Accent,
            Foreground = ThemePalette.TextBright,
            BorderThickness = new Thickness(0),
            FontSize = 12,
        };
        btn.Click += async (_, _) =>
        {
            var router = App.Services?.GetService<IRouter>();
            if (router != null)
                await router.NavigateAsync("/models");
        };
        panel.Children.Add(btn);

        panel.Children.Add(new TextBlock
        {
            Text = SdkLocalizer.Loc("Hint_AiMode"),
            FontSize = 11, Foreground = ThemePalette.TextMuted, TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    // ============================================================
    // ③ 统计行
    // ============================================================

    /// <summary>布局枚举 → 下拉索引</summary>
    private static int LayoutToIndex(TranslationLayout layout) => layout switch
    {
        TranslationLayout.Mirrored => 1,
        TranslationLayout.SingleFile => 2,
        _ => 0,
    };

    /// <summary>产出 diff 审批面板（默认隐藏；HasPending 为真时显示，展示整轮 diff + 应用/放弃按钮）</summary>
    private Control CreatePreviewPanel()
    {
        var discardButton = UiBuilders.ActionButton(SdkLocalizer.Loc("Action_Discard"), _vm.DiscardSyncCommand, isGhost: true);
        var approveButton = UiBuilders.ActionButton(SdkLocalizer.Loc("Action_Apply"), _vm.ApproveSyncCommand, isPrimary: true);

        var header = new DockPanel { LastChildFill = true };
        header.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = S,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = SdkLocalizer.Loc("Tr_PreviewTitle"), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = ThemePalette.Title },
                new TextBlock { Text = SdkLocalizer.Loc("Tr_PreviewSub"), FontSize = 11, Foreground = ThemePalette.TextMuted },
            }
        });
        var btnGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = S, VerticalAlignment = VerticalAlignment.Center };
        btnGroup.Children.Add(discardButton);
        btnGroup.Children.Add(approveButton);
        DockPanel.SetDock(btnGroup, Dock.Right);
        header.Children.Add(btnGroup);

        _previewText = new TextBox
        {
            Text = _vm.PreviewText,
            IsReadOnly = true,
            FontFamily = FontFamily.Parse("Consolas, Cascadia Code, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 180,
            Background = ThemePalette.EditorBgDark,
            Foreground = ThemePalette.Text,
            BorderBrush = ThemePalette.Border,
        };

        return new Border
        {
            IsVisible = false,
            Background = ThemePalette.CardBg,
            BorderBrush = ThemePalette.Warn,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(M, S, M, 0),
            Padding = new Thickness(M, S, M, S),
            Child = new StackPanel
            {
                Spacing = S,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children = { header, _previewText },
            },
        };
    }

    private Control CreateStatRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = S,
            Margin = new Thickness(M, S, M, 0),
        };
        _statText = new TextBlock
        {
            Text = SdkLocalizer.Loc("Tr_Stat", _vm.ScannedCount, _vm.TranslatedCount, _vm.UntranslatedCount),
            FontSize = 12, Foreground = ThemePalette.TextMuted,
        };
        row.Children.Add(_statText);
        return row;
    }

    // ============================================================
    // 列表条目
    // ============================================================

    private Control CreateEntryRow(TranslationEntry? entry)
    {
        // 防御：Avalonia 模板构建器可能收到 null（集合变化/容器回收时）
        if (entry == null)
            return new TextBlock();

        var isTranslated = entry.IsTranslated;
        var dotColor = isTranslated
            ? Color.FromArgb(220, 78, 201, 176)   // 绿色圆点
            : Color.FromArgb(220, 230, 165, 60);  // 橙色圆点

        var row = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
            Margin = new Thickness(0, 1),
            ColumnSpacing = S,
            Children =
            {
                // 左侧状态小圆点
                new Ellipse
                {
                    Width = 8, Height = 8,
                    Fill = new SolidColorBrush(dotColor),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                // 文本
                new TextBlock
                {
                    Text = entry.Text, TextWrapping = TextWrapping.Wrap,
                    Foreground = ThemePalette.Text, VerticalAlignment = VerticalAlignment.Center, FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                },
                // 徽章
                new Border
                {
                    Background = new SolidColorBrush(isTranslated
                        ? Color.FromArgb(30, 78, 201, 176)
                        : Color.FromArgb(30, 230, 165, 60)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = isTranslated ? SdkLocalizer.Loc("Word_Translated") : SdkLocalizer.Loc("Word_Untranslated"),
                        FontSize = 11,
                        Foreground = isTranslated ? ThemePalette.Success : ThemePalette.Warn,
                    },
                },
            }
        };
        Grid.SetColumn(row.Children[0], 0);
        Grid.SetColumn(row.Children[1], 1);
        Grid.SetColumn(row.Children[2], 2);
        return row;
    }

    // ============================================================
    // 辅助构建器（统一间距/宽度体系）
    // ============================================================

    /// <summary>带标签的语言根目录下拉（工具栏）：locale code 预设 + 可自由输入，遵循引擎 i18n 标准（Lang/{lang}/）。</summary>
    private static Control LabeledLocaleCombo(string initial, System.Action<string> onChanged)
    {
        var wrap = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Margin = new Thickness(M, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        wrap.Children.Add(new TextBlock { Text = SdkLocalizer.Loc("Tr_TargetLang"), FontSize = 11, Foreground = ThemePalette.TextMuted });

        var combo = new ComboBox
        {
            ItemsSource = new[] { "zh-CN", "en-US", "ja-JP", "ko-KR", "zh-TW", "fr-FR", "de-DE", "es-ES", "ru-RU" },
            Text = initial,
            IsEditable = true,
            FontSize = 12,
            MinWidth = 110,
            MaxWidth = 160,
            PlaceholderText = SdkLocalizer.Loc("Ph_LangCode"),
        };
        ToolTip.SetTip(combo, SdkLocalizer.Loc("Tip_LangCode"));

        // 选预设 → 直接采用；自由输入 → SelectedItem 为 null 时取 Text
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string s) onChanged(s);
        };
        combo.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty && combo.SelectedItem == null)
                onChanged(combo.Text ?? "");
        };

        wrap.Children.Add(combo);
        return wrap;
    }
}
