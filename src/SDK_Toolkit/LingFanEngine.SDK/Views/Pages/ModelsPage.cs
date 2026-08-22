using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;
using LingFanEngine.SDK.Themes;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views.Controls;
using MFToolkit.Routing.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 模型管理页（.axaml 外壳解码）——布局在 ModelsPage.axaml；
/// 条目模板（含分组头、默认模型高亮徽章、供应商徽章）在 code-behind 构建。
/// </summary>
public partial class ModelsPage : UserControl, INavigationAware
{
    private ModelsViewModel? _vm;
    private bool _subscribedToCollection;

    public ModelsPage()
    {
        InitializeComponent();
        WireButtons();
    }

    // ===== INavigationAware =====

    /// <summary>
    /// 获取 ViewModel：优先当前 DataContext（DataContext 是 Router 用同一 DI 实例注入、并负责 axaml 绑定）；
    /// 由于 Router 触发本页 OnNavigated 先于 WorkspaceWindow 给 DataContext 赋值，首次导航时可能为 null，
    /// 此时从 App.Services 兜底取同一单例，保证增删改操作落在被绑定的那个实例上。
    /// </summary>
    private ModelsViewModel? GetViewModel() =>
        DataContext as ModelsViewModel ?? App.Services?.GetService<ModelsViewModel>();

    public void OnNavigated(Dictionary<string, object?>? parameters)
    {
        // 复用已持有的 VM，避免每次导航重建而丢失绑定引用
        var vm = GetViewModel();
        if (vm != null && !ReferenceEquals(_vm, vm))
        {
            _vm = vm;
            _vm.Refresh();
        }
        else
        {
            _vm?.Refresh();
        }
        // ItemTemplate 不依赖 DataContext，独立设置（若尚未设置；ListBox.ItemsSource 已由 axaml 绑定 GroupedItems）
        SetupItemTemplate();
        UpdateButtons();
        if (!_subscribedToCollection && _vm != null)
        {
            _subscribedToCollection = true;
            _vm.GroupedItems.CollectionChanged += (_, _) => UpdateButtons();
        }
    }

    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    private void WireButtons()
    {
        AddBtn.Click += (_, _) => OpenDialog(null);
        EditBtn.Click += (_, _) => EditSelected();
        DelBtn.Click += (_, _) => DeleteSelected();
        DefBtn.Click += (_, _) => SetDefaultSelected();
        // 分组头不可选中：切换后若不是 ModelConfig 则清空
        ModelList.SelectionChanged += (_, _) =>
        {
            if (ModelList.SelectedItem != null && ModelList.SelectedItem is not ModelConfig)
                ModelList.SelectedItem = null;
            UpdateButtons();
        };
    }

    /// <summary>
    /// 条目模板（类型分派）；仅设置一次。
    /// <para>运行时元素 = <see cref="ProviderGroupHeader"/> | <see cref="ModelConfig"/>。</para>
    /// </summary>
    private void SetupItemTemplate()
    {
        if (ModelList.ItemTemplate != null)
            return;
        ModelList.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
        {
            return item switch
            {
                VendorGroupHeader h => CreateGroupHeader(h),
                ModelConfig m => CreateItemRow(m),
                _ => new Control()
            };
        }, true);
    }

    /// <summary>供应商分组头（跨整行的 Label 样式，不可选中）</summary>
    private Control CreateGroupHeader(VendorGroupHeader h)
    {
        var title = new TextBlock
        {
            Text = h.Vendor,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = ThemePalette.Accent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var count = new TextBlock
        {
            Text = $"共 {h.Count} 个模型",
            FontSize = 11,
            Foreground = ThemePalette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var icon = new TextBlock
        {
            Text = "\uE774",  // Segoe MDL2 = 地球（泛指"供应商/接口"）
            FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = ThemePalette.Accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var line = new Border
        {
            Height = 1,
            Background = ThemePalette.Border,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        var root = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto"),
            ColumnSpacing = 0,
            Margin = new Thickness(0, 12, 0, 4),
            IsHitTestVisible = false,    // 分组头不可选中、不可交互
            Children =
            {
                icon, title, line, count,
            }
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(line, 2);
        Grid.SetColumn(count, 3);
        return root;
    }

    /// <summary>模型条目（左：文字；右：供应商徽章 + 默认大徽章）。默认模型带强调背景。</summary>
    private Control CreateItemRow(ModelConfig? m)
    {
        if (m == null)
            return new Control();

        var isDefault = _vm != null && m.Id == _vm.DefaultId;
        var providerLabel = m.Provider == ModelProvider.Anthropic ? "Anthropic" : "OpenAI";

        // ---- 左侧文本（Grid 替代 StackPanel 确保宽度约束逐层传递）----
        var title = new TextBlock
        {
            Text = m.DisplayOrId,
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = isDefault ? ThemePalette.TextBright : ThemePalette.Text,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        };
        var sub = new TextBlock
        {
            Text = $"{providerLabel} · {m.ModelId} · {m.BaseUrl}",
            FontSize = 11,
            Foreground = isDefault
                ? new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
                : ThemePalette.TextMuted,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        };
        var textCol = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto"),
            RowSpacing = 2,
            Margin = new Thickness(12, 8, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { title, sub },
        };
        Grid.SetRow(title, 0);
        Grid.SetRow(sub, 1);

        // ---- 右侧徽章 ----
        var providerBadge = UiBuilders.StatusBadge(providerLabel, isPositive: m.Provider == ModelProvider.OpenAi);
        // 默认模型用更醒目的大徽章（★ 默认 实心填充）
        var defaultBadge = CreateDefaultBadge();
        defaultBadge.IsVisible = isDefault;
        var badges = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Children = { providerBadge, defaultBadge },
        };

        // ---- 行根布局（BUG FIX：显式 Grid.SetColumn 分派列）----
        var rowGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
        };
        rowGrid.Children.Add(textCol);
        Grid.SetColumn(textCol, 0);
        rowGrid.Children.Add(badges);
        Grid.SetColumn(badges, 1);

        // 默认模型：强调背景（Accent 半透明）+ 左侧 Accent 竖条 + 2px Accent 外边框
        if (isDefault)
        {
            // 取 ThemePalette.Accent 的颜色并降低不透明度，始终与当前主题的 Accent 令牌同色
            var accentColor = ((SolidColorBrush)ThemePalette.Accent).Color;
            var accentTint = new SolidColorBrush(Color.FromArgb(
                40, accentColor.R, accentColor.G, accentColor.B));
            // 默认模型标题下的副文本用"亮白减淡"，在任何深色 Accent 背景上都可读
            var brightMuted = new SolidColorBrush(Color.FromArgb(
                210, 255, 255, 255));
            sub.Foreground = brightMuted;

            var accentBar = new Border
            {
                Background = ThemePalette.Accent,
                CornerRadius = new CornerRadius(4, 0, 0, 4),
            };
            var highlightGrid = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("3,*"),
            };
            highlightGrid.Children.Add(accentBar);
            Grid.SetColumn(accentBar, 0);
            highlightGrid.Children.Add(rowGrid);
            Grid.SetColumn(rowGrid, 1);

            return new Border
            {
                Background = accentTint,
                BorderBrush = ThemePalette.Accent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 2),
                Child = highlightGrid,
            };
        }

        // 非默认模型：普通卡片样式
        return new Border
        {
            Background = ThemePalette.CardBg,
            BorderBrush = ThemePalette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 2),
            Child = rowGrid,
        };
    }

    /// <summary>"★ 默认"强调徽章（比 StatusBadge 更大、实心 Accent、带星标）</summary>
    private static Border CreateDefaultBadge()
    {
        return new Border
        {
            Background = ThemePalette.Accent,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "\uE735  默认",  // Segoe MDL2 = ★ 收藏星标
                FontFamily = FontFamily.Parse("Segoe MDL2 Assets"),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = ThemePalette.TextBright,
            }
        };
    }

    private void UpdateButtons()
    {
        if (_vm == null) return;
        var sel = ModelList.SelectedItem as ModelConfig;
        var has = sel != null;
        EditBtn.IsEnabled = has;
        DelBtn.IsEnabled = has;
        DefBtn.IsEnabled = has && sel != null && sel.Id != _vm.DefaultId;
        EmptyHint.IsVisible = _vm.Models.Count == 0;
    }

    private void OpenDialog(ModelConfig? source)
    {
        // 由 DI 解析（单例：连通性服务/模型服务），不在 View 内 new 有副作用依赖。
        var connectivity = App.Services?.GetService<ModelConnectivityService>();
        var modelService = App.Services?.GetService<IModelService>();
        if (connectivity == null || modelService == null)
        {
            System.Diagnostics.Debug.WriteLine("[ModelsPage] 缺少 DI 服务，无法打开模型对话框");
            return;
        }

        var dlg = new Common.ModelEditDialog(source, connectivity, modelService);
        dlg.Closed += (_, _) =>
        {
            if (dlg.ResultModel != null)
                _vm?.Upsert(dlg.ResultModel);
            _vm?.Refresh();
        };

        var owner = this.VisualRoot as Window;
        if (owner != null)
            dlg.ShowDialog(owner);
        else
            dlg.Show();
    }

    private void EditSelected()
    {
        if (ModelList.SelectedItem is ModelConfig m)
            OpenDialog(m);
    }

    private void DeleteSelected()
    {
        if (ModelList.SelectedItem is ModelConfig m)
            _vm?.Remove(m.Id);
    }

    private void SetDefaultSelected()
    {
        if (ModelList.SelectedItem is ModelConfig m)
            _vm?.SetDefault(m.Id);
    }
}
