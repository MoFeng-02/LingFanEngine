using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;
using LingFanEngine.SDK.Themes;

namespace LingFanEngine.SDK.Views.Common;

/// <summary>
/// 「自定义模型」对话框——1:1 对标标准 AI 客户端模型添加界面。
/// <para>支持 OpenAI Chat Completions / Anthropic Messages 两种 API 格式；
/// 含 Base URL / 模型 ID / 展示名称（带计数）/ API Key（👁 切换）/ 高级配置折叠 / 连通性测试。</para>
/// <para>结果通过 <see cref="ResultModel"/> 返回（null=取消）。</para>
/// </summary>
public sealed class ModelEditDialog : Window
{
    private readonly ModelConfig _editing;
    private readonly ModelConnectivityService _connectivity;
    private readonly IModelService _modelService;

    private ComboBox _providerCombo = null!;
    private TextBox _baseUrlBox = null!;
    private TextBox _modelIdBox = null!;
    private TextBox _displayBox = null!;
    private TextBlock _counter = null!;
    private TextBox _keyBox = null!;
    private Button _eyeBtn = null!;
    private bool _keyVisible;
    private NumericUpDown _tempBox = null!;
    private NumericUpDown _batchBox = null!;
    private NumericUpDown _timeoutBox = null!;
    private TextBlock _statusText = null!;
    private Button _addBtn = null!;

    // 「获取模型列表」的勾选清单（默认全选，添加所选而非一次性全注册）
    private StackPanel _fetchPanel = null!;
    private TextBlock _fetchCountText = null!;
    private ListBox _fetchList = null!;
    private Button _addSelectedBtn = null!;
    private readonly List<CheckBox> _fetchChecks = new();

    /// <summary>编辑结果（确认后非空，取消为 null）</summary>
    public ModelConfig? ResultModel { get; private set; }

    /// <summary>创建对话框（source=null 为新增，否则编辑其克隆）</summary>
    public ModelEditDialog(ModelConfig? source, ModelConnectivityService connectivity, IModelService modelService)
    {
        _editing = source == null ? new ModelConfig() : Clone(source);
        _connectivity = connectivity;
        _modelService = modelService;

        Title = "自定义模型";
        Width = 560;
        Height = 560;
        MinWidth = 460;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemePalette.EditorBg;
        InitializeComponent();
    }

    // ===== 暗色主题色板（与 WorkspaceWindow/TranslationPage 一致）=====
    // 统一色板：取自 Themes/Colors.axaml（C# 经 ThemePalette.* 读取）——不再页内硬编码。

    private void InitializeComponent()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var root = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 16),
            Spacing = 14,
        };

        // ===== 标题栏（自定义：标题 + ×）=====
        var header = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Margin = new Thickness(0, 0, 0, 4),
        };
        header.Children.Add(new TextBlock
        {
            Text = "自定义模型",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = ThemePalette.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(header.Children[0], 0);

        var closeBtn = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = ThemePalette.TextMuted,
            FontSize = 16,
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeBtn.Click += (_, _) => Close(false);
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(closeBtn);
        // 拖拽移动
        header.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        root.Children.Add(header);

        // ===== API 格式 =====
        root.Children.Add(CreateLabeled("API 格式 *"));
        _providerCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "OpenAI Chat Completions", "Anthropic Messages" },
            SelectedIndex = _editing.Provider == ModelProvider.Anthropic ? 1 : 0,
        };
        _providerCombo.SelectionChanged += (_, _) =>
        {
            _editing.Provider = _providerCombo.SelectedIndex == 1 ? ModelProvider.Anthropic : ModelProvider.OpenAi;
            // 切换时按提供方给默认 Base URL（仅当用户未手填时）
            if (string.IsNullOrWhiteSpace(_baseUrlBox.Text))
                _baseUrlBox.Text = DefaultBaseUrl(_editing.Provider);
        };
        root.Children.Add(_providerCombo);

        // ===== Base URL =====
        root.Children.Add(CreateLabeled("Base URL"));
        _baseUrlBox = new TextBox
        {
            Text = _editing.BaseUrl,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
        };
        root.Children.Add(_baseUrlBox);
        root.Children.Add(new TextBlock
        {
            Text = "例如 https://api.openai.com/v1",
            FontSize = 11,
            Foreground = ThemePalette.TextMuted,
        });

        // ===== 模型 ID（选填：拉列表后自动批量添加；手填用于不支持列表的私有端点）=====
        root.Children.Add(CreateLabeled("模型 ID（选填）"));
        _modelIdBox = new TextBox
        {
            Text = _editing.ModelId,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
            PlaceholderText = "可选 · 留空则改用「获取模型列表」批量添加",
        };
        _modelIdBox.TextChanged += (_, _) => UpdateAddEnabled();
        root.Children.Add(_modelIdBox);

        // ===== 模型展示名称（选填 + 计数）=====
        var nameRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Margin = new Thickness(0, 4, 0, 0),
        };
        nameRow.Children.Add(CreateLabeled("模型展示名称"));
        Grid.SetColumn(nameRow.Children[0], 0);
        _counter = new TextBlock
        {
            Text = "0/32",
            FontSize = 11,
            Foreground = ThemePalette.TextMuted,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(_counter, 1);
        nameRow.Children.Add(_counter);
        root.Children.Add(nameRow);

        _displayBox = new TextBox
        {
            Text = _editing.DisplayName,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
            MaxLength = 32,
            PlaceholderText = "选填，留空则列表显示模型 ID",
        };
        _displayBox.TextChanged += (_, _) => _counter.Text = $"{_displayBox.Text.Length}/32";
        root.Children.Add(_displayBox);

        // ===== API 密钥（密码框 + 👁）=====
        root.Children.Add(CreateLabeled("API 密钥 *"));
        var keyRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        _keyBox = new TextBox
        {
            Text = _editing.ApiKey,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13,
            PasswordChar = '*',
        };
        Grid.SetColumn(_keyBox, 0);
        _eyeBtn = new Button
        {
            Content = "👁",
            Width = 36,
            Margin = new Thickness(6, 0, 0, 0),
            Background = ThemePalette.PanelBg,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            Foreground = ThemePalette.Text,
        };
        _eyeBtn.Click += (_, _) =>
        {
            _keyVisible = !_keyVisible;
            _keyBox.PasswordChar = _keyVisible ? '\0' : '*';
        };
        Grid.SetColumn(_eyeBtn, 1);
        keyRow.Children.Add(_keyBox);
        keyRow.Children.Add(_eyeBtn);
        root.Children.Add(keyRow);

        // ===== 高级配置（折叠）=====
        var expander = new Expander
        {
            Header = "高级配置",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = ThemePalette.Text,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
        };
        var adv = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        adv.Children.Add(CreateLabeled("温度（0-1，建议 0.2-0.3）"));
        _tempBox = new NumericUpDown
        {
            Value = (decimal)_editing.Advanced.Temperature,
            Minimum = 0,
            Maximum = 1,
            Increment = 0.05m,
            FormatString = "F2",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        adv.Children.Add(_tempBox);
        adv.Children.Add(CreateLabeled("每批条数（1-200）"));
        _batchBox = new NumericUpDown
        {
            Value = _editing.Advanced.BatchSize,
            Minimum = 1,
            Maximum = 200,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        adv.Children.Add(_batchBox);
        adv.Children.Add(CreateLabeled("超时（秒）"));
        _timeoutBox = new NumericUpDown
        {
            Value = _editing.Advanced.TimeoutSeconds,
            Minimum = 5,
            Maximum = 600,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        adv.Children.Add(_timeoutBox);
        expander.Content = adv;
        root.Children.Add(expander);

        // ===== 连通性测试 + 获取模型列表 =====
        var testRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        var testBtn = new Button
        {
            Content = "连通性测试",
            Padding = new Thickness(14, 6),
            Background = ThemePalette.PanelBg,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            Foreground = ThemePalette.Text,
            FontSize = 12,
        };
        testBtn.Click += async (_, _) => await RunConnectivityTestAsync();
        var fetchBtn = new Button
        {
            Content = "获取模型列表",
            Padding = new Thickness(14, 6),
            Background = ThemePalette.PanelBg,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            Foreground = ThemePalette.Text,
            FontSize = 12,
        };
        fetchBtn.Click += async (_, _) => await FetchModelsAsync();
        testRow.Children.Add(testBtn);
        testRow.Children.Add(fetchBtn);
        root.Children.Add(testRow);

        // ===== 「获取模型列表」勾选清单（默认隐藏，获取成功后展示）=====
        _fetchPanel = new StackPanel
        {
            Spacing = 6,
            IsVisible = false,
        };

        var fetchHeader = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto,Auto"),
        };
        fetchHeader.Children.Add(new TextBlock
        {
            Text = "已获取模型（勾选要添加的）",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = ThemePalette.Title,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(fetchHeader.Children[0], 0);
        _fetchCountText = new TextBlock
        {
            FontSize = 11,
            Foreground = ThemePalette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_fetchCountText, 1);
        fetchHeader.Children.Add(_fetchCountText);

        var selectAllBtn = new Button
        {
            Content = "全选",
            Padding = new Thickness(8, 2),
            Background = Brushes.Transparent,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            Foreground = ThemePalette.TextHover,
            FontSize = 11,
        };
        selectAllBtn.Click += (_, _) => SetAllFetchChecks(true);
        var deselectAllBtn = new Button
        {
            Content = "全不选",
            Padding = new Thickness(8, 2),
            Background = Brushes.Transparent,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            Foreground = ThemePalette.TextHover,
            FontSize = 11,
            Margin = new Thickness(6, 0, 0, 0),
        };
        deselectAllBtn.Click += (_, _) => SetAllFetchChecks(false);
        var selectRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { selectAllBtn, deselectAllBtn },
        };
        Grid.SetColumn(selectRow, 2);
        fetchHeader.Children.Add(selectRow);
        _fetchPanel.Children.Add(fetchHeader);

        _fetchList = new ListBox
        {
            MaxHeight = 200,
            Background = ThemePalette.PanelBg,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
        };
        _fetchPanel.Children.Add(_fetchList);

        _addSelectedBtn = new Button
        {
            Content = "添加所选",
            Padding = new Thickness(14, 6),
            Background = ThemePalette.Accent,
            BorderThickness = new Thickness(0),
            Foreground = ThemePalette.TextBright,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _addSelectedBtn.Click += async (_, _) => await AddCheckedModelsAsync();
        _fetchPanel.Children.Add(_addSelectedBtn);

        root.Children.Add(_fetchPanel);

        _statusText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 16,
        };
        root.Children.Add(_statusText);

        // ===== 信息提示（GET /v1/models 为零成本、不消耗 Token，无需警示）=====
        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, 138, 138, 138)),
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            Child = new TextBlock
            {
                Text = "ℹ 连通性测试与「获取模型列表」均调用 GET /v1/models，零成本、不消耗 Token；拉取后勾选要添加的（默认全选），添加时按「供应商+端点+模型」去重。",
                FontSize = 11,
                Foreground = ThemePalette.TextMuted,
                TextWrapping = TextWrapping.Wrap,
            },
        });

        // ===== 底部按钮 =====
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var cancelBtn = new Button
        {
            Content = "取消",
            Padding = new Thickness(16, 6),
            Background = Brushes.Transparent,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            Foreground = ThemePalette.Text,
            FontSize = 12,
        };
        cancelBtn.Click += (_, _) => Close(false);
        var resetBtn = new Button
        {
            Content = "重置",
            Padding = new Thickness(16, 6),
            Background = Brushes.Transparent,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            Foreground = ThemePalette.Text,
            FontSize = 12,
        };
        resetBtn.Click += (_, _) => ResetToEditing();
        _addBtn = new Button
        {
            Content = "添加模型",
            Padding = new Thickness(16, 6),
            Background = ThemePalette.Accent,
            BorderThickness = new Thickness(0),
            Foreground = ThemePalette.TextBright,
            FontSize = 12,
        };
        _addBtn.Click += (_, _) => Commit();
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(resetBtn);
        btnRow.Children.Add(_addBtn);
        root.Children.Add(btnRow);

        scroll.Content = root;
        Content = scroll;

        UpdateAddEnabled();
    }

    private static TextBlock CreateLabeled(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = ThemePalette.TextMuted,
        Margin = new Thickness(0, 6, 0, 2),
    };

    private static string DefaultBaseUrl(ModelProvider provider) =>
        provider == ModelProvider.Anthropic ? "https://api.anthropic.com" : "https://api.openai.com/v1";

    private void UpdateAddEnabled()
    {
        _addBtn.IsEnabled = !string.IsNullOrWhiteSpace(_modelIdBox.Text);
    }

    private void ResetToEditing()
    {
        _providerCombo.SelectedIndex = _editing.Provider == ModelProvider.Anthropic ? 1 : 0;
        _baseUrlBox.Text = _editing.BaseUrl;
        _modelIdBox.Text = _editing.ModelId;
        _displayBox.Text = _editing.DisplayName;
        _keyBox.Text = _editing.ApiKey;
        _tempBox.Value = (decimal)_editing.Advanced.Temperature;
        _batchBox.Value = _editing.Advanced.BatchSize;
        _timeoutBox.Value = _editing.Advanced.TimeoutSeconds;
        _statusText.Text = "";
        UpdateAddEnabled();
    }

    private ModelConfig BuildFromControls()
    {
        return new ModelConfig
        {
            Id = _editing.Id,
            Provider = _providerCombo.SelectedIndex == 1 ? ModelProvider.Anthropic : ModelProvider.OpenAi,
            BaseUrl = _baseUrlBox.Text?.Trim() ?? "",
            ModelId = _modelIdBox.Text?.Trim() ?? "",
            DisplayName = _displayBox.Text?.Trim() ?? "",
            ApiKey = _keyBox.Text ?? "",
            Advanced = new ModelAdvancedConfig
            {
                Temperature = (double)(_tempBox.Value ?? 0.3m),
                BatchSize = (int)(_batchBox.Value ?? 50m),
                TimeoutSeconds = (int)(_timeoutBox.Value ?? 120m),
            },
        };
    }

    /// <summary>「获取模型列表」：拉取供应商 GET /v1/models 的 id 列表并展示勾选清单（默认全选）。</summary>
    private async Task FetchModelsAsync()
    {
        var model = BuildFromControls();
        if (string.IsNullOrWhiteSpace(model.BaseUrl))
        {
            _statusText.Text = "请先填写 Base URL";
            _statusText.Foreground = ThemePalette.Warn;
            return;
        }

        _statusText.Text = "正在获取模型列表...";
        _statusText.Foreground = ThemePalette.TextMuted;
        try
        {
            var result = await _connectivity.TestAsync(model, CancellationToken.None);
            if (!result.IsSuccess)
            {
                _statusText.Text = $"❌ 无法获取列表：{result.Message}";
                _statusText.Foreground = ThemePalette.Warn;
                return;
            }
            if (result.ModelIds.Count == 0)
            {
                _fetchPanel.IsVisible = false;
                _statusText.Text = "✅ 已连通，但该端点未返回模型列表（data[] 为空）。请改用「模型 ID」手动添加。";
                _statusText.Foreground = ThemePalette.Warn;
                return;
            }

            // 展示勾选清单（默认全选），让用户挑出要添加的，而不是一次性全注册
            PopulateFetchList(result.ModelIds);
            _fetchPanel.IsVisible = true;
            _statusText.Text = $"已获取 {result.ModelIds.Count} 个模型，勾选要添加的，然后点击「添加所选」。";
            _statusText.Foreground = ThemePalette.TextMuted;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"❌ 获取异常：{ex.Message}";
            _statusText.Foreground = ThemePalette.Warn;
        }
    }

    /// <summary>用模型 id 列表填充勾选清单（默认全选）。</summary>
    private void PopulateFetchList(IReadOnlyList<string> ids)
    {
        _fetchChecks.Clear();
        _fetchList.Items.Clear();
        foreach (var id in ids)
        {
            var check = new CheckBox
            {
                Content = id,
                FontSize = 13,
                IsChecked = true,
            };
            _fetchChecks.Add(check);
            _fetchList.Items.Add(check);
        }
        _fetchCountText.Text = $"共 {ids.Count} 个";
        _addSelectedBtn.Content = $"添加所选（{ids.Count}）";
    }

    private void SetAllFetchChecks(bool value)
    {
        foreach (var c in _fetchChecks)
            c.IsChecked = value;
        _addSelectedBtn.Content = $"添加所选（{_fetchChecks.Count(c => c.IsChecked == true)}）";
    }

    /// <summary>添加勾选中的模型（继承当前 Provider / BaseUrl / ApiKey / 高级配置，按「供应商+端点+模型」去重）。</summary>
    private async Task AddCheckedModelsAsync()
    {
        var model = BuildFromControls();
        var selected = _fetchChecks.Where(c => c.IsChecked == true).Select(c => c.Content?.ToString() ?? "").ToList();
        if (selected.Count == 0)
        {
            _statusText.Text = "请至少勾选一个模型";
            _statusText.Foreground = ThemePalette.Warn;
            return;
        }

        _statusText.Text = "正在添加所选模型...";
        _statusText.Foreground = ThemePalette.TextMuted;
        try
        {
            var temp = (double)(_tempBox.Value ?? 0.3m);
            var batch = (int)(_batchBox.Value ?? 50m);
            var timeout = (int)(_timeoutBox.Value ?? 120m);
            var incoming = selected.Select(id => new ModelConfig
            {
                Id = Guid.NewGuid().ToString(),
                Provider = model.Provider,
                BaseUrl = model.BaseUrl.Trim(),
                ModelId = id,
                DisplayName = "",
                ApiKey = model.ApiKey,
                Advanced = new ModelAdvancedConfig
                {
                    Temperature = temp,
                    BatchSize = batch,
                    TimeoutSeconds = timeout,
                },
            }).ToList();

            _modelService.RegisterModels(incoming, out var added, out var skipped);
            _fetchPanel.IsVisible = false;
            _statusText.Text = $"✅ 已添加 {added} 个模型 · 跳过已存在 {skipped}";
            _statusText.Foreground = ThemePalette.Success;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"❌ 添加异常：{ex.Message}";
            _statusText.Foreground = ThemePalette.Warn;
        }

        await Task.CompletedTask;
    }

    private void Commit()
    {
        if (string.IsNullOrWhiteSpace(_modelIdBox.Text))
            return;
        ResultModel = BuildFromControls();
        Close(true);
    }

    private async Task RunConnectivityTestAsync()
    {
        var model = BuildFromControls();
        if (string.IsNullOrWhiteSpace(model.BaseUrl))
        {
            _statusText.Text = "请先填写 Base URL";
            _statusText.Foreground = ThemePalette.Warn;
            return;
        }
        _statusText.Text = "测试中...";
        _statusText.Foreground = ThemePalette.TextMuted;
        try
        {
            var result = await _connectivity.TestAsync(model, CancellationToken.None);
            _statusText.Text = result.IsSuccess
                ? $"✅ {result.Message}"
                : $"❌ {result.Message}";
            _statusText.Foreground = result.IsSuccess ? ThemePalette.Success : ThemePalette.Warn;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"❌ 测试异常：{ex.Message}";
            _statusText.Foreground = ThemePalette.Warn;
        }
    }

    private static ModelConfig Clone(ModelConfig src) => new()
    {
        Id = src.Id,
        Provider = src.Provider,
        BaseUrl = src.BaseUrl,
        ModelId = src.ModelId,
        DisplayName = src.DisplayName,
        ApiKey = src.ApiKey,
        Advanced = new ModelAdvancedConfig
        {
            Temperature = src.Advanced.Temperature,
            BatchSize = src.Advanced.BatchSize,
            TimeoutSeconds = src.Advanced.TimeoutSeconds,
        },
    };
}
