using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace _LingFanEngineTemplateTitle_.UI;

/// <summary>
/// 游戏内底部工具栏（模板层 UI）
/// <para>只在 Game 场景显示，提供返回/历史/跳过/自动/保存/快存/快读/设置快捷入口。</para>
/// <para>叠在 SceneView 之上，不受对话模态遮罩影响。</para>
/// </summary>
public class GameToolbar : UserControl
{
    private readonly IStateContainer _state;
    private readonly IGameController? _controller;
    private readonly OverlayManager? _overlay;

    private readonly Button _skipBtn;
    private readonly Button _autoBtn;

    // 缓存上帧状态避免重复刷新
    private bool _lastSkipActive;
    private bool _lastAutoActive;

    public GameToolbar(IStateContainer state, IGameController? controller, OverlayManager? overlay)
    {
        _state = state;
        _controller = controller;
        _overlay = overlay;

        var bar = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 15, 15, 25)),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = -2, Blur = 8, Color = Color.FromArgb(80, 0, 0, 0) })
        };

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        // 返回（回溯）
        AddBtn(panel, "↩ 返回", OnBackClick);
        // 历史
        AddBtn(panel, "📜 历史", OnHistoryClick);
        // 分隔线
        AddSeparator(panel);
        // 跳过
        _skipBtn = AddBtn(panel, "⏭ 跳过", OnSkipClick);
        // 自动
        _autoBtn = AddBtn(panel, "▶ 自动", OnAutoClick);
        // 分隔线
        AddSeparator(panel);
        // 保存
        AddBtn(panel, "💾 保存", OnSaveClick);
        // 快速存档
        AddBtn(panel, "⚡ 快存", OnQuickSaveClick);
        // 快速读档
        AddBtn(panel, "⚡ 快读", OnQuickLoadClick);
        // 分隔线
        AddSeparator(panel);
        // 设置
        AddBtn(panel, "⚙ 设置", OnSettingsClick);

        bar.Child = panel;

        var root = new Panel();
        root.Children.Add(bar);
        root.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.VerticalAlignment = VerticalAlignment.Bottom;

        Content = root;
        IsVisible = false;
    }

    /// <summary>每帧更新（由 MainView 帧回调调用）</summary>
    public void Update(double delta)
    {
        var sceneType = (SceneType)_state.Get<int>(StateKeys.Scene.CurrentType);
        var anyPanelVisible = _overlay?.AnyPanelVisible() ?? false;

        // 可见性：仅 Game 场景 + 无覆盖面板时显示
        var shouldShow = sceneType == SceneType.Game && !anyPanelVisible;
        if (IsVisible != shouldShow)
            IsVisible = shouldShow;

        if (!IsVisible) return;

        // Skip/Auto 状态刷新
        var skipActive = _state.Get<bool>(StateKeys.Playback.SkipActive);
        var autoActive = _state.Get<bool>(StateKeys.Playback.AutoActive);

        if (skipActive != _lastSkipActive)
        {
            _skipBtn.Content = skipActive ? "⏭ 跳过 [开]" : "⏭ 跳过";
            _lastSkipActive = skipActive;
        }
        if (autoActive != _lastAutoActive)
        {
            _autoBtn.Content = autoActive ? "▶ 自动 [开]" : "▶ 自动";
            _lastAutoActive = autoActive;
        }
    }

    // ========== 按钮事件 ==========

    private void OnBackClick()
    {
        _ = _controller?.RollbackAsync();
    }

    private void OnHistoryClick()
    {
        _overlay?.ShowHistoryPanel();
    }

    private void OnSkipClick()
    {
        _controller?.ToggleSkip();
    }

    private void OnAutoClick()
    {
        _controller?.ToggleAuto();
    }

    private void OnSaveClick()
    {
        _overlay?.ShowSavePanel();
    }

    private void OnQuickSaveClick()
    {
        _controller?.Save("quick_save");
        _state.Set(StateKeys.Notify.Text, "已快速保存");
        _state.Set(StateKeys.Notify.Type, "info");
    }

    private void OnQuickLoadClick()
    {
        _controller?.Load("quick_save");
    }

    private void OnSettingsClick()
    {
        _overlay?.ShowSettingsPanel();
    }

    // ========== 控件构建辅助 ==========

    private Button AddBtn(StackPanel parent, string label, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            FontSize = 13,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(1, 0),
            Background = new SolidColorBrush(Color.FromArgb(60, 60, 60, 80)),
            BorderThickness = new Thickness(0),
            Foreground = Brushes.WhiteSmoke,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        btn.PointerEntered += (_, _) =>
            btn.Background = new SolidColorBrush(Color.FromArgb(120, 80, 120, 180));
        btn.PointerExited += (_, _) =>
            btn.Background = new SolidColorBrush(Color.FromArgb(60, 60, 60, 80));

        btn.Click += (_, _) => onClick();

        parent.Children.Add(btn);
        return btn;
    }

    private void AddSeparator(StackPanel parent)
    {
        parent.Children.Add(new Border
        {
            Width = 1,
            Background = new SolidColorBrush(Color.FromArgb(60, 80, 80, 100)),
            Margin = new Thickness(4, 4)
        });
    }
}
