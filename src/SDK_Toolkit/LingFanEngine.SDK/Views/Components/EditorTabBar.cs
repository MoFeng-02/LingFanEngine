using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Views.Components;

/// <summary>
/// P1-5: 编辑器多标签页栏
/// <para>使用 ItemsControl + FuncDataTemplate 自动响应集合变化，避免手动重建控件导致的事件订阅泄漏。</para>
/// </summary>
public class EditorTabBar : UserControl
{
    private readonly ObservableCollection<OpenFileTab> _tabs = [];

    private static readonly IBrush ActiveTabBg = new SolidColorBrush(Color.Parse("#1E1E1E"));
    private static readonly IBrush InactiveTabBg = new SolidColorBrush(Color.Parse("#2D2D2D"));
    private static readonly IBrush ActiveTabFg = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush InactiveTabFg = new SolidColorBrush(Color.Parse("#969696"));
    private static readonly IBrush CloseBtnNormalFg = new SolidColorBrush(Color.Parse("#969696"));

    /// <summary>切换标签时触发</summary>
    public event Action<OpenFileTab>? TabSwitched;

    /// <summary>关闭标签时触发</summary>
    public event Action<OpenFileTab>? TabCloseRequested;

    /// <summary>当前标签列表</summary>
    public ObservableCollection<OpenFileTab> Tabs => _tabs;

    public EditorTabBar()
    {
        var itemsControl = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
            }),
            ItemTemplate = new FuncDataTemplate<OpenFileTab>((tab, _) => CreateTabContent(tab), false),
        };
        itemsControl.ItemsSource = _tabs;

        var scrollViewer = new ScrollViewer
        {
            Content = itemsControl,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Background = new SolidColorBrush(Color.Parse("#252526")),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        Content = scrollViewer;
    }

    /// <summary>为每个标签创建内容控件（由 FuncDataTemplate 调用，控件生命周期由 Avalonia 管理）</summary>
    private Control CreateTabContent(OpenFileTab? tab)
    {
        if (tab == null) return new Control();

        var nameText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var closeBtn = new Button
        {
            Content = "×",
            FontSize = 14,
            Padding = new Thickness(4, 0),
            MinWidth = 20,
            MinHeight = 20,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = CloseBtnNormalFg,
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = tab,
            Classes = { "transparent" },
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { nameText, closeBtn },
        };

        var tabButton = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#1E1E1E")),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(12, 4, 4, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = tab,
            Child = panel,
        };

        // 初始外观
        UpdateTabAppearance(tabButton, nameText, tab);

        // 点击 Border 切换标签
        tabButton.PointerPressed += (s, e) =>
        {
            if (s is Border b && b.Tag is OpenFileTab t)
            {
                TabSwitched?.Invoke(t);
            }
        };

        // 关闭按钮——阻止事件冒泡，避免同时触发标签切换
        closeBtn.Click += (s, e) =>
        {
            e.Handled = true;
            if (s is Button btn && btn.Tag is OpenFileTab t)
            {
                TabCloseRequested?.Invoke(t);
            }
        };

        // 监听标签属性变化更新外观
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OpenFileTab.IsActive) or nameof(OpenFileTab.IsDirty)
                or nameof(OpenFileTab.DisplayName) or nameof(OpenFileTab.FileName))
            {
                UpdateTabAppearance(tabButton, nameText, tab);
            }
        };

        return tabButton;
    }

    private static void UpdateTabAppearance(Border tabButton, TextBlock nameText, OpenFileTab tab)
    {
        tabButton.Background = tab.IsActive ? ActiveTabBg : InactiveTabBg;
        nameText.Foreground = tab.IsActive ? ActiveTabFg : InactiveTabFg;
        nameText.Text = tab.DisplayName;
    }
}
