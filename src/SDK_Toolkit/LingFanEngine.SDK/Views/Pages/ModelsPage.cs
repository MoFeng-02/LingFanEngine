using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;
using Microsoft.Extensions.DependencyInjection;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 模型管理页面——列出「自定义模型」，支持新增/编辑/删除/设为默认。
/// <para>新增/编辑通过 <see cref="Common.ModelEditDialog"/> 模态窗口完成。</para>
/// </summary>
public class ModelsPage : UserControl, INavigationAware
{
    private readonly ModelsViewModel _vm;
    private ListBox _listBox = null!;
    private TextBlock _emptyHint = null!;

    private static readonly IBrush s_bg = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush s_panelBg = new SolidColorBrush(Color.Parse("#252526"));
    private static readonly IBrush s_border = new SolidColorBrush(Color.Parse("#3C3C3C"));
    private static readonly IBrush s_text = new SolidColorBrush(Color.Parse("#D4D4D4"));
    private static readonly IBrush s_muted = new SolidColorBrush(Color.Parse("#8A8A8A"));
    private static readonly IBrush s_accent = new SolidColorBrush(Color.Parse("#0E639C"));
    private static readonly IBrush s_success = new SolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush s_warn = new SolidColorBrush(Color.Parse("#CE9178"));

    public ModelsPage(ModelsViewModel viewModel)
    {
        _vm = viewModel;
        InitializeComponent();
    }

    public void OnNavigated(Dictionary<string, object?>? parameters) => _vm.Refresh();
    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    private void InitializeComponent()
    {
        var scroll = new ScrollViewer
        {
            Background = s_bg,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
        };

        // 标题 + 添加按钮
        var topRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        topRow.Children.Add(new TextBlock
        {
            Text = "模型管理",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Foreground = s_text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(topRow.Children[0], 0);

        var addBtn = new Button
        {
            Content = "添加模型",
            Padding = new Thickness(16, 6),
            Background = s_accent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            FontSize = 12,
        };
        addBtn.Click += (_, _) => OpenDialog(null);
        Grid.SetColumn(addBtn, 1);
        topRow.Children.Add(addBtn);
        root.Children.Add(topRow);

        root.Children.Add(new TextBlock
        {
            Text = "管理 AI 翻译模型（OpenAI 兼容 / Anthropic），配置保存在本地 models.json。",
            FontSize = 12,
            Foreground = s_muted,
            TextWrapping = TextWrapping.Wrap,
        });

        // 列表
        _listBox = new ListBox
        {
            Background = s_panelBg,
            BorderBrush = s_border,
            BorderThickness = new Thickness(1),
            MinHeight = 200,
            ItemsSource = _vm.Models,
            ItemTemplate = new FuncDataTemplate<ModelConfig>((m, _) => CreateItemRow(m), true),
        };
        _listBox.SelectionChanged += (_, _) => UpdateActionButtons();
        root.Children.Add(_listBox);

        _emptyHint = new TextBlock
        {
            Text = "尚未添加任何模型。点击右上角「添加模型」按标准对话框添加。",
            FontSize = 12,
            Foreground = s_muted,
            Margin = new Thickness(0, 4),
        };
        root.Children.Add(_emptyHint);

        // 操作按钮行
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        var editBtn = new Button { Content = "编辑", Padding = new Thickness(14, 6), FontSize = 12, Tag = "edit" };
        var delBtn = new Button { Content = "删除", Padding = new Thickness(14, 6), FontSize = 12, Tag = "del" };
        var defBtn = new Button { Content = "设为默认", Padding = new Thickness(14, 6), FontSize = 12, Tag = "def" };
        editBtn.Click += (_, _) => EditSelected();
        delBtn.Click += (_, _) => DeleteSelected();
        defBtn.Click += (_, _) => SetDefaultSelected();
        actions.Children.Add(editBtn);
        actions.Children.Add(delBtn);
        actions.Children.Add(defBtn);
        root.Children.Add(actions);

        scroll.Content = root;
        Content = scroll;

        UpdateActionButtons();
        _vm.Models.CollectionChanged += (_, _) => UpdateActionButtons();
    }

    private Control CreateItemRow(ModelConfig m)
    {
        var isDefault = m.Id == _vm.DefaultId;
        var row = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Margin = new Thickness(0, 2),
            ColumnSpacing = 8,
        };
        var left = new StackPanel { Spacing = 2, Margin = new Thickness(8, 6) };
        left.Children.Add(new TextBlock
        {
            Text = m.DisplayOrId,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = s_text,
        });
        left.Children.Add(new TextBlock
        {
            Text = $"{(m.Provider == ModelProvider.Anthropic ? "Anthropic Messages" : "OpenAI Chat Completions")} · {m.ModelId}",
            FontSize = 11,
            Foreground = s_muted,
        });
        Grid.SetColumn(left, 0);

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 78, 201, 176)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "默认", FontSize = 11, Foreground = s_success },
        };
        badge.IsVisible = isDefault;
        Grid.SetColumn(badge, 1);

        row.Children.Add(left);
        row.Children.Add(badge);
        return row;
    }

    private void UpdateActionButtons()
    {
        var has = _listBox.SelectedItem is ModelConfig;
        // 子按钮通过 Children 查找
        if (Content is ScrollViewer sv && sv.Content is StackPanel sp)
        {
            var actions = sp.Children.OfType<StackPanel>().LastOrDefault();
            if (actions != null)
            {
                foreach (var c in actions.Children.OfType<Button>())
                {
                    if (c.Tag is string tag && tag == "def")
                    {
                        var sel = _listBox.SelectedItem as ModelConfig;
                        c.IsEnabled = has && sel != null && sel.Id != _vm.DefaultId;
                    }
                    else
                        c.IsEnabled = has;
                }
            }
        }
        _emptyHint.IsVisible = _vm.Models.Count == 0;
    }

    private void OpenDialog(ModelConfig? source)
    {
        var connectivity = App.Services?.GetService<ModelConnectivityService>();
        connectivity ??= new ModelConnectivityService();
        var modelService = App.Services?.GetService<IModelService>();
        modelService ??= new ModelService();
        var dlg = new Common.ModelEditDialog(source, connectivity, modelService)
        {
            // 以当前窗口为宿主
        };
        // 必须在 ShowDialog 之前订阅：ShowDialog 是模态，会阻塞到窗口关闭，
        // 若在此之后订阅 Closed，回调将永远不会触发（编辑/自动注册结果丢失）。
        dlg.Closed += (_, _) =>
        {
            // 手动「添加模型」走 ResultModel → Upsert；「获取模型列表」直接写入 modelService。
            // 无论哪条路径都 Refresh：从单例服务重新拉取，覆盖两种结果。
            if (dlg.ResultModel != null)
                _vm.Upsert(dlg.ResultModel);
            _vm.Refresh();
        };

        var owner = this.VisualRoot as Window;
        if (owner != null)
            dlg.ShowDialog(owner);
        else
            dlg.Show();
    }

    private void EditSelected()
    {
        if (_listBox.SelectedItem is ModelConfig m)
            OpenDialog(m);
    }

    private void DeleteSelected()
    {
        if (_listBox.SelectedItem is ModelConfig m)
            _vm.Remove(m.Id);
    }

    private void SetDefaultSelected()
    {
        if (_listBox.SelectedItem is ModelConfig m)
            _vm.SetDefault(m.Id);
    }
}
