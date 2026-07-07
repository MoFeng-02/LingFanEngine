using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Entry.UI;

/// <summary>
/// 右键快捷菜单（Demo 层 UI）
/// <para>对标 Ren'Py 的右键快捷菜单，提供常用操作入口。</para>
/// <para>通过右键点击触发，包含存档/读档/设置/历史/跳过/自动/返回标题等选项。</para>
/// </summary>
public class QuickMenuPanel : UserControl
{
    private readonly IStateContainer _state;
    private readonly IGameController? _controller;

    /// <summary>菜单项点击事件</summary>
    public event Action<string>? MenuItemSelected;

    /// <summary>面板关闭事件</summary>
    public event Action? Closed;

    public QuickMenuPanel(IStateContainer state, IGameController? controller)
    {
        _state = state;
        _controller = controller;

        // 透明背景，仅在菜单区域可见
        var mainPanel = new Panel();

        // 点击空白区域关闭菜单
        mainPanel.PointerPressed += (_, _) => { Hide(); Closed?.Invoke(); };

        // 菜单容器
        var menuBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 25, 25, 35)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var menuPanel = new StackPanel { Orientation = Orientation.Vertical };

        // 菜单项
        AddMenuItem(menuPanel, "💾 存档", "save");
        AddMenuItem(menuPanel, "📂 读档", "load");
        AddMenuSeparator(menuPanel);
        AddMenuItem(menuPanel, "📜 对话历史", "history");
        AddMenuItem(menuPanel, "🖼 CG鉴赏", "gallery");
        AddMenuItem(menuPanel, "⚙ 设置", "settings");
        AddMenuSeparator(menuPanel);
        AddMenuItem(menuPanel, "⏭ 跳过模式", "skip");
        AddMenuItem(menuPanel, "▶ 自动模式", "auto");
        AddMenuSeparator(menuPanel);
        AddMenuItem(menuPanel, "🏠 返回标题", "title");
        AddMenuItem(menuPanel, "🔧 调试控制台", "debug");
        AddMenuItem(menuPanel, "🚪 退出游戏", "exit");

        menuBorder.Child = menuPanel;
        mainPanel.Children.Add(menuBorder);
        Content = mainPanel;
        IsVisible = false;
    }

    /// <summary>显示菜单</summary>
    public void Show()
    {
        IsVisible = true;
        UpdateSkipAutoLabels();
    }

    /// <summary>隐藏菜单</summary>
    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>更新跳过/自动模式标签</summary>
    private void UpdateSkipAutoLabels()
    {
        var skipActive = _state.Get<bool>(StateKeys.Playback.SkipActive);
        var autoActive = _state.Get<bool>(StateKeys.Playback.AutoActive);

        // 找到菜单面板并更新标签
        if (Content is Panel panel && panel.Children.Count > 0 && panel.Children[0] is Border border
            && border.Child is StackPanel menuPanel)
        {
            int idx = 0;
            foreach (var child in menuPanel.Children)
            {
                if (child is Button btn)
                {
                    if (btn.Tag is string tag)
                    {
                        if (tag == "skip")
                            btn.Content = skipActive ? "⏭ 跳过模式 [开]" : "⏭ 跳过模式 [关]";
                        else if (tag == "auto")
                            btn.Content = autoActive ? "▶ 自动模式 [开]" : "▶ 自动模式 [关]";
                    }
                    idx++;
                }
            }
        }
    }

    /// <summary>添加菜单项</summary>
    private void AddMenuItem(StackPanel parent, string label, string tag)
    {
        var btn = new Button
        {
            Content = label,
            Tag = tag,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(15, 8),
            Margin = new Thickness(2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 14
        };

        btn.Click += (_, _) =>
        {
            HandleMenuAction(tag);
            Hide();
            Closed?.Invoke();
        };

        parent.Children.Add(btn);
    }

    /// <summary>添加分隔线</summary>
    private void AddMenuSeparator(StackPanel parent)
    {
        parent.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(60, 80, 80, 100)),
            Margin = new Thickness(5, 2, 5, 2)
        });
    }

    /// <summary>处理菜单动作</summary>
    private void HandleMenuAction(string action)
    {
        MenuItemSelected?.Invoke(action);

        switch (action)
        {
            case "skip":
                _controller?.ToggleSkip();
                break;
            case "auto":
                _controller?.ToggleAuto();
                break;
            case "title":
                // 使用 BackTitleAlias 别名，由 NavigateHandler 自动重定向到 TitleSceneName
                _controller?.Navigate("back_title");
                break;
            case "debug":
                // debug 由外层 OverlayManager 处理
                MenuItemSelected?.Invoke("debug");
                break;
            case "exit":
                Environment.Exit(0);
                break;
            // save, load, history, settings 由外层 OverlayManager 处理
        }
    }
}
