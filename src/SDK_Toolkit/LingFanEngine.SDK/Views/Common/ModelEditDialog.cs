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
        Background = s_bg;
        InitializeComponent();
    }

    // ===== 暗色主题色板（与 WorkspaceWindow/TranslationPage 一致）=====
    private static readonly IBrush s_bg = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush s_panelBg = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush s_border = new SolidColorBrush(Color.Parse("#3C3C3C"));
    private static readonly IBrush s_text = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush s_muted = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush s_accent = new SolidColorBrush(Color.Parse("#0E639C"));
    private static readonly IBrush s_warn = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush s_success = new SolidColorBrush(Color.Parse("#4EC9B0"));

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
            Foreground = s_text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(header.Children[0], 0);

        var closeBtn = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = s_muted,
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
            Foreground = s_muted,
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
            Foreground = s_muted,
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
            Background = s_panelBg,
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            Foreground = s_text,
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
            Foreground = s_text,
            BorderBrush = s_border,
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
            Background = s_panelBg,
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            Foreground = s_text,
            FontSize = 12,
        };
        testBtn.Click += async (_, _) => await RunConnectivityTestAsync();
        var fetchBtn = new Button
        {
            Content = "获取模型列表",
            Padding = new Thickness(14, 6),
            Background = s_panelBg,
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            Foreground = s_text,
            FontSize = 12,
        };
        fetchBtn.Click += async (_, _) => await FetchModelsAsync();
        testRow.Children.Add(testBtn);
        testRow.Children.Add(fetchBtn);
        root.Children.Add(testRow);
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
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6),
            Child = new TextBlock
            {
                Text = "ℹ 连通性测试与「获取模型列表」均调用 GET /v1/models，零成本、不消耗 Token；自动注册按「供应商+端点+模型」去重。",
                FontSize = 11,
                Foreground = s_muted,
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
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            Foreground = s_text,
            FontSize = 12,
        };
        cancelBtn.Click += (_, _) => Close(false);
        var resetBtn = new Button
        {
            Content = "重置",
            Padding = new Thickness(16, 6),
            Background = Brushes.Transparent,
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            Foreground = s_text,
            FontSize = 12,
        };
        resetBtn.Click += (_, _) => ResetToEditing();
        _addBtn = new Button
        {
            Content = "添加模型",
            Padding = new Thickness(16, 6),
            Background = s_accent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
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
        Foreground = s_muted,
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

    /// <summary>「获取模型列表」：拉取供应商 GET /v1/models 的 id 列表并批量自动注册（去重）。</summary>
    private async Task FetchModelsAsync()
    {
        var model = BuildFromControls();
        if (string.IsNullOrWhiteSpace(model.BaseUrl))
        {
            _statusText.Text = "请先填写 Base URL";
            _statusText.Foreground = s_warn;
            return;
        }

        _statusText.Text = "正在获取模型列表...";
        _statusText.Foreground = s_muted;
        try
        {
            var result = await _connectivity.TestAsync(model, CancellationToken.None);
            if (!result.IsSuccess)
            {
                _statusText.Text = $"❌ 无法获取列表：{result.Message}";
                _statusText.Foreground = s_warn;
                return;
            }
            if (result.ModelIds.Count == 0)
            {
                _statusText.Text = "✅ 已连通，但该端点未返回模型列表（data[] 为空）。请改用「模型 ID」手动添加。";
                _statusText.Foreground = s_warn;
                return;
            }

            // 继承当前对话框的 Provider / BaseUrl / ApiKey / 高级配置
            var temp = (double)(_tempBox.Value ?? 0.3m);
            var batch = (int)(_batchBox.Value ?? 50m);
            var timeout = (int)(_timeoutBox.Value ?? 120m);
            var incoming = result.ModelIds.Select(id => new ModelConfig
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
            _statusText.Text = $"✅ 已获取 {result.ModelIds.Count} 个模型 · 新增 {added} · 已存在跳过 {skipped}";
            _statusText.Foreground = s_success;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"❌ 获取异常：{ex.Message}";
            _statusText.Foreground = s_warn;
        }
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
            _statusText.Foreground = s_warn;
            return;
        }
        _statusText.Text = "测试中...";
        _statusText.Foreground = s_muted;
        try
        {
            var result = await _connectivity.TestAsync(model, CancellationToken.None);
            _statusText.Text = result.IsSuccess
                ? $"✅ {result.Message}"
                : $"❌ {result.Message}";
            _statusText.Foreground = result.IsSuccess ? s_success : s_warn;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"❌ 测试异常：{ex.Message}";
            _statusText.Foreground = s_warn;
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
