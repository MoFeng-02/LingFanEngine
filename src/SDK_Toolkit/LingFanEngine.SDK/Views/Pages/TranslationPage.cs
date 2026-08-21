using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.ViewModels;
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

    // ===== 暗色主题色板（与 WorkspaceWindow 一致）=====
    private static readonly IBrush s_bg = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush s_panelBg = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush s_border = new SolidColorBrush(Color.Parse("#3C3C3C"));
    private static readonly IBrush s_text = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush s_muted = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush s_accent = new SolidColorBrush(Color.Parse("#0E639C"));
    private static readonly IBrush s_success = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush s_warn = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush s_title = new SolidColorBrush(Color.Parse("#4FC1FF"));
    private static readonly IBrush s_cardBg = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));

    public TranslationPage(TranslationViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public void OnNavigated(Dictionary<string, object?>? parameters)
    {
        // 从「模型管理」返回时刷新模型列表与选中态
        _vm.RefreshModels();
    }
    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    private void InitializeComponent()
    {
        // ===== 骨架：6 行严格分栏（列表 * 弹性占满）=====
        var root = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,Auto,*,Auto"),
            Background = s_bg,
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

        _entriesList = new ListBox
        {
            Margin = new Thickness(M, S, M, S),
            Background = s_panelBg,
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ItemsSource = _vm.Entries,
            Padding = new Thickness(S, S),
        };
        _entriesList.ItemTemplate = new FuncDataTemplate<TranslationEntry>((entry, _) => CreateEntryRow(entry), true);
        root.Children.Add(_entriesList);
        Grid.SetRow(_entriesList, 4);

        _statusText = new TextBlock
        {
            Text = _vm.StatusMessage, TextWrapping = TextWrapping.Wrap,
            Foreground = s_muted, FontSize = 12, Margin = new Thickness(M, 0, M, S),
        };
        root.Children.Add(_statusText);
        Grid.SetRow(_statusText, 5);

        // 订阅 ViewModel 状态变化
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
                    _aiConfigPanel.IsVisible = _vm.Mode == TranslationMode.Ai;
                    _apiConfigPanel.IsVisible = _vm.Mode == TranslationMode.Api;
                    break;
                case nameof(TranslationViewModel.ScannedCount):
                case nameof(TranslationViewModel.TranslatedCount):
                case nameof(TranslationViewModel.UntranslatedCount):
                    _statText.Text = $"共 {_vm.ScannedCount} 条 · 已翻译 {_vm.TranslatedCount} · 待翻译 {_vm.UntranslatedCount}";
                    break;
            }
        };

        Content = root;
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

        var sourceField = LabeledTextBox("源语言", _vm.SourceLang, v => _vm.SourceLang = v, width: 80, watermark: "留空=自动检测");
        sourceField.Margin = new Thickness(0, 0, M, 0);
        DockPanel.SetDock(sourceField, Dock.Right);
        row1.Children.Add(sourceField);

        // 目标语言（locale code：en-US / ja-JP / ko-KR…，遵循引擎 i18n 标准，作 Lang/{lang}/ 语言根目录名）
        var targetField = LabeledLocaleCombo(_vm.TargetLang, v => _vm.TargetLang = v);
        targetField.Margin = new Thickness(0, 0, M, 0);
        DockPanel.SetDock(targetField, Dock.Right);
        row1.Children.Add(targetField);

        // 标题（占满剩余，左对齐垂直居中）
        row1.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "多语言翻译", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = s_text },
                new TextBlock
                {
                    Text = "原文即 key · 自动提取 .story + C# 文本 · 批量 AI 翻译",
                    FontSize = 11, Foreground = s_muted,
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2),
                },
            }
        });

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
        _syncButton = ActionButton("同步翻译", _vm.SyncCommand, isPrimary: true);
        _cancelButton = ActionButton("取消", _vm.CancelSyncCommand, isCancel: true);
        _cancelButton.IsVisible = false;
        actions.Children.Add(ActionButton("扫描文本", _vm.ScanCommand, isGhost: true));
        actions.Children.Add(_syncButton);
        actions.Children.Add(_cancelButton);
        DockPanel.SetDock(actions, Dock.Right);
        row2.Children.Add(actions);

        // 模式选择
        var modeField = LabeledCombo("翻译模式", ["人工翻译", "AI 智能翻译", "翻译 API"], 0, v =>
        {
            _vm.Mode = v switch
            {
                1 => TranslationMode.Ai,
                2 => TranslationMode.Api,
                _ => TranslationMode.Manual,
            };
        });
        modeField.Margin = new Thickness(0, 0, M, 0);
        DockPanel.SetDock(modeField, Dock.Right);
        row2.Children.Add(modeField);

        // 强制重翻译（不勾=跳过已翻译，勾=全部重翻）
        var forceCheck = new CheckBox
        {
            Content = "强制重翻译",
            IsChecked = _vm.ForceRetranslate,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = s_muted,
            Margin = new Thickness(0, 0, M, 0),
        };
        ToolTip.SetTip(forceCheck, "勾选后忽略已有译文，全部条目重新翻译；不勾则跳过已翻译条目");
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
            Background = s_panelBg,
            BorderBrush = s_border,
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
                Card(
                    "AI 翻译模型（OpenAI 兼容 / Anthropic；从「模型管理」添加）",
                    FormGrid(
                        FormRow("模型", CreateModelCombo())),
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
                Card(
                    "翻译 API 配置 · DeepL 风格（tag_handling 自动保留富文本标记）",
                    FormGrid(
                        FormRow("端点", CreateTextBox(_vm.ApiEndpoint, v => _vm.ApiEndpoint = v)),
                        FormRow("API Key", CreateTextBox(_vm.ApiKey, v => _vm.ApiKey = v, isPassword: true)),
                        FormRow("目标语言码", CreateTextBox(_vm.ApiTargetLangCode, v => _vm.ApiTargetLangCode = v))),
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
            PlaceholderText = "请选择模型",
        };
        SyncModelComboSelection();
        _modelCombo.SelectionChanged += (_, _) =>
        {
            if (_modelCombo.SelectedItem is ModelConfig m)
                _vm.SelectedModelId = m.Id;
        };
        // 模型列表变化（如从管理页返回）后重新同步选中项
        _vm.Models.CollectionChanged += (_, _) => SyncModelComboSelection();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TranslationViewModel.SelectedModelId))
                SyncModelComboSelection();
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
            Content = "管理模型",
            Padding = new Thickness(14, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = s_accent,
            Foreground = Brushes.White,
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
            Text = "AI 模式按所选模型批量翻译（占位符自动校验）；模型配置保存在本地 models.json。",
            FontSize = 11, Foreground = s_muted, TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    // ============================================================
    // ③ 统计行
    // ============================================================

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
            Text = $"共 {_vm.ScannedCount} 条 · 已翻译 {_vm.TranslatedCount} · 待翻译 {_vm.UntranslatedCount}",
            FontSize = 12, Foreground = s_muted,
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
                    Foreground = s_text, VerticalAlignment = VerticalAlignment.Center, FontSize = 13,
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
                        Text = isTranslated ? "已翻译" : "未翻译",
                        FontSize = 11,
                        Foreground = isTranslated ? s_success : s_warn,
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

    /// <summary>卡片容器：标题 + 内容 + 可选底部说明</summary>
    private static Control Card(string title, Control body, Control? footer)
    {
        var content = new StackPanel { Spacing = S, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12, FontWeight = FontWeight.Bold,
            Foreground = s_title,
        });
        content.Children.Add(body);
        if (footer != null)
            content.Children.Add(footer);

        return new Border
        {
            Background = s_cardBg,
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(M, S + 4, M, S + 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content,
        };
    }

    /// <summary>
    /// 表单网格（单列 N 行，每行一个 <see cref="FormRow"/> 占满整行）。
    /// <para>关键：必须是 <c>*</c> 单列，让 FormRow 整体拿到父容器全宽；
    /// 若仍为 <c>{LabelW},*</c> 双列，FormRow 会被卡进 100px 第 0 列，
    /// 其内部 Grid 自身又要 100px 标签 + * 输入，* 部分宽度 = 0 → NumericUpDown/TextBox 被压扁、label 文字被遮挡。</para>
    /// </summary>
    private static Grid FormGrid(params Control[] rows)
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
    private static Control FormRow(string label, Control input)
    {
        Grid.SetColumn(input, 1);
        input.Margin = new Thickness(S, 2, 0, 2);
        input.VerticalAlignment = VerticalAlignment.Center;
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (input is TextBox tb) tb.MinWidth = 220;
        if (input is ComboBox cb) cb.MinWidth = 200;
        if (input is NumericUpDown nud) nud.MinWidth = 120;

        var labelBlock = new TextBlock
        {
            Text = label + ":", Foreground = s_muted, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, S, 0),
        };
        Grid.SetColumn(labelBlock, 0);

        return new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse($"{LabelW},*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { labelBlock, input },
        };
    }

    /// <summary>操作按钮（三种形态：主/幽灵/取消）</summary>
    private static Button ActionButton(string text, ICommand? cmd, bool isPrimary = false, bool isGhost = false, bool isCancel = false)
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
            btn.Background = s_accent;
            btn.Foreground = Brushes.White;
            btn.BorderThickness = new Thickness(0);
        }
        else if (isGhost || isCancel)
        {
            btn.Classes.Add("transparent");
        }
        return btn;
    }

    /// <summary>带标签的下拉框（工具栏）</summary>
    private static Control LabeledCombo(string label, string[] items, int selected, System.Action<int> onSelect)
    {
        var wrap = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Margin = new Thickness(M, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        wrap.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = s_muted });
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = selected, FontSize = 12, Width = 140 };
        combo.SelectionChanged += (_, _) => onSelect(combo.SelectedIndex);
        wrap.Children.Add(combo);
        return wrap;
    }

    /// <summary>带标签的文本框（工具栏）</summary>
    private static Control LabeledTextBox(string label, string initial, System.Action<string> onChanged, double width, string? watermark = null)
    {
        var wrap = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Margin = new Thickness(M, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        wrap.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = s_muted });
        var box = new TextBox { Text = initial, FontSize = 12, Width = width, PlaceholderText = watermark };
        box.TextChanged += (_, _) => onChanged(box.Text ?? initial);
        wrap.Children.Add(box);
        return wrap;
    }

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
        wrap.Children.Add(new TextBlock { Text = "目标语言", FontSize = 11, Foreground = s_muted });

        var combo = new ComboBox
        {
            ItemsSource = new[] { "zh-CN", "en-US", "ja-JP", "ko-KR", "zh-TW", "fr-FR", "de-DE", "es-ES", "ru-RU" },
            Text = initial,
            IsEditable = true,
            FontSize = 12,
            MinWidth = 110,
            MaxWidth = 160,
            PlaceholderText = "en-US / ja-JP",
        };
        ToolTip.SetTip(combo, "语言根目录名（引擎标准：en-US/ja-JP/ko-KR…），决定 Lang/{lang}/ 目录、语言切换与可用语言列表");

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

    /// <summary>表单文本框（输入列星号拉伸，HorizontalAlignment=Stretch 自动占满）</summary>
    private static TextBox CreateTextBox(string initial, System.Action<string> setter, bool isPassword = false)
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
    private static NumericUpDown CreateNumericBox(int initial, System.Action<int> setter, int min, int max)
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
}
