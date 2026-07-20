using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;

namespace _LingFanEngineTemplateTitle_.UI;

/// <summary>
/// 右键快捷菜单（Demo 层 UI）
/// <para>对标 Ren'Py 的右键快捷菜单，提供常用操作入口。</para>
/// <para>通过右键点击触发，包含存档/读档/设置/历史/跳过/自动/返回标题等选项。</para>
/// </summary>
public class QuickMenuPanel : UserControl
{
    private readonly IStateContainer _state;
    private readonly IGameController? _controller;

    // 直接持有需要动态更新的按钮引用（P2-4: 替代控件树遍历）
    private Button? _skipButton;
    private Button? _autoButton;

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
        _skipButton = AddMenuItem(menuPanel, "⏭ 跳过模式", "skip", closeOnClick: false);
        _autoButton = AddMenuItem(menuPanel, "▶ 自动模式", "auto", closeOnClick: false);
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

    /// <summary>更新跳过/自动模式标签（P2-4: 直接用字段引用，不再遍历控件树）</summary>
    private void UpdateSkipAutoLabels()
    {
        var skipActive = _state.Get<bool>(StateKeys.Playback.SkipActive);
        var autoActive = _state.Get<bool>(StateKeys.Playback.AutoActive);

        if (_skipButton != null)
            _skipButton.Content = skipActive ? "⏭ 跳过模式 [开]" : "⏭ 跳过模式 [关]";
        if (_autoButton != null)
            _autoButton.Content = autoActive ? "▶ 自动模式 [开]" : "▶ 自动模式 [关]";
    }

    /// <summary>添加菜单项</summary>
    /// <param name="closeOnClick">点击后是否关闭菜单（skip/auto 为 false，不关闭）</param>
    private Button AddMenuItem(StackPanel parent, string label, string tag, bool closeOnClick = true)
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

            if (closeOnClick)
            {
                Hide();
                Closed?.Invoke();
            }
            else
            {
                // toggle 类操作（skip/auto）不关闭菜单，仅刷新标签
                UpdateSkipAutoLabels();
            }
        };

        parent.Children.Add(btn);
        return btn;
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
        // P1-4: 统一在此处触发事件，不在 case 内重复触发
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
            case "exit":
                // P1-5: 优雅退出，不再使用 Environment.Exit(0)
                DoExit();
                break;
            // save, load, history, settings, debug 由外层 OverlayManager 处理
        }
    }

    /// <summary>优雅退出应用（P1-5）</summary>
    private static void DoExit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            // 移动端/Browser 等非桌面平台的退出
            Environment.Exit(0);
        }
    }
}
