using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

/// <summary>
/// 覆盖层渲染器——管理菜单/输入/通知/性能HUD/对话遮罩等覆盖在场景之上的 UI 层。
/// </summary>
internal sealed class OverlayRenderer : IOverlayRenderer
{
    private const double NotifyFadeDuration = 0.3;

    private readonly IStateContainer _state;

    private Panel? _sceneRoot;
    private Grid? _outerGrid;
    private Border? _dialogMask;

    // 菜单/输入覆盖层
    private Panel? _menuPanel;
    private Panel? _inputPanel;
    private TextBox? _inputBox;

    // 通知 Toast
    private Control? _currentNotify;
    private double _notifyRemainSeconds;
    private double _notifyFadeSeconds;

    // 性能 HUD
    private TextBlock? _perfHud;

    public OverlayRenderer(IStateContainer state)
    {
        _state = state;
    }

    public void Attach(Panel? sceneRoot, Grid? outerGrid, Border? dialogMask)
    {
        _sceneRoot = sceneRoot;
        _outerGrid = outerGrid;
        _dialogMask = dialogMask;
    }

    public void Detach()
    {
        // 清理菜单/输入面板
        if (_menuPanel != null) _sceneRoot?.Children.Remove(_menuPanel);
        if (_inputPanel != null) _sceneRoot?.Children.Remove(_inputPanel);
        _menuPanel = null;
        _inputPanel = null;
        _inputBox = null;

        // 清理通知
        if (_currentNotify != null && _outerGrid != null)
        {
            for (int i = _outerGrid.Children.Count - 1; i >= 0; i--)
                if (_outerGrid.Children[i].Tag?.ToString() == StateKeys.UiTags.Notify)
                    _outerGrid.Children.RemoveAt(i);
        }
        _currentNotify = null;

        // 清理性能 HUD
        if (_perfHud != null)
        {
            _outerGrid?.Children.Remove(_perfHud);
            _perfHud = null;
        }

        _sceneRoot = null;
        _outerGrid = null;
        _dialogMask = null;
    }

    public void Update(double delta)
    {
        UpdateMenuOverlay();
        UpdateInputOverlay();
        UpdateNotifyToast(delta);
        UpdatePerformanceHud();
        UpdateDialogMask();
    }

    // ========== 菜单覆盖层 ==========

    private void UpdateMenuOverlay()
    {
        var opts = _state.Get<object>(StateKeys.Menu.Options) ?? _state.Get<object>(StateKeys.Menu.DslOptions);
        var prompt = _state.Get<string>(StateKeys.Menu.Prompt) ?? _state.Get<string>(StateKeys.Menu.DslPrompt);
        if (opts is string[] optsArr && optsArr.Length > 0)
        {
            if (_menuPanel != null) return;
            var root = _sceneRoot;
            if (root == null) return;
            _menuPanel = new Panel
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _menuPanel.SetValue(Grid.ZIndexProperty, 150);
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = prompt ?? "", Foreground = Brushes.White, FontSize = 24, Margin = new Thickness(0, 0, 0, 20) });
            for (int i = 0; i < optsArr.Length; i++)
            {
                var idx = i;
                var btn = new Button { Content = optsArr[i], Width = 300, Height = 44, Margin = new Thickness(0, 5), Background = new SolidColorBrush(Color.FromArgb(200, 80, 140, 255)) };
                btn.Click += (_, _) => { _state.Set(StateKeys.Dialog.Complete, false); _state.Set(StateKeys.Menu.Selected, idx); };
                stack.Children.Add(btn);
            }
            _menuPanel.Children.Add(stack);
            root.Children.Add(_menuPanel);
        }
        else if (_menuPanel != null)
        {
            _sceneRoot?.Children.Remove(_menuPanel);
            _menuPanel = null;
        }
    }

    // ========== 输入覆盖层 ==========

    private void UpdateInputOverlay()
    {
        var prompt = _state.Get<string>(StateKeys.Input.Prompt) ?? _state.Get<string>(StateKeys.Input.DslPrompt);
        if (!string.IsNullOrEmpty(prompt))
        {
            if (_inputPanel != null) return;
            var root = _sceneRoot;
            if (root == null) return;
            _inputPanel = new Panel
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _inputPanel.SetValue(Grid.ZIndexProperty, 150);
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = prompt, Foreground = Brushes.White, FontSize = 20, Margin = new Thickness(0, 0, 0, 15) });
            _inputBox = new TextBox { Width = 400, Height = 40, FontSize = 18, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(100, 50, 50, 50)) };
            stack.Children.Add(_inputBox);
            var submitBtn = new Button { Content = "确定", Width = 120, Height = 40, Margin = new Thickness(0, 10), HorizontalAlignment = HorizontalAlignment.Center };
            submitBtn.Click += (_, _) => { _state.Set(StateKeys.Dialog.Complete, false); _state.Set<object?>(StateKeys.Input.Result, _inputBox?.Text ?? ""); };
            stack.Children.Add(submitBtn);
            _inputPanel.Children.Add(stack);
            root.Children.Add(_inputPanel);
            _inputBox.Focus();
        }
        else if (_inputPanel != null)
        {
            _sceneRoot?.Children.Remove(_inputPanel);
            _inputPanel = null; _inputBox = null;
        }
    }

    // ========== 通知 Toast ==========

    private void UpdateNotifyToast(double delta)
    {
        var text = _state.Get<string>(StateKeys.Notify.Text);

        if (text != null)
        {
            var type = _state.Get<string>(StateKeys.Notify.Type) ?? "info";
            var root = _outerGrid;
            if (root == null) return;

            RemoveNotifyToast(root);

            var (icon, bg, fg) = type switch
            {
                "warning" => ("[!]",
                    new SolidColorBrush(Color.FromArgb(200, 180, 120, 0)),
                    Brushes.White),
                "error" => ("[X]",
                    new SolidColorBrush(Color.FromArgb(200, 160, 30, 30)),
                    Brushes.White),
                _ => ("[i]",
                    new SolidColorBrush(Color.FromArgb(200, 30, 60, 100)),
                    Brushes.White),
            };

            var notify = new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 80, 0, 0),
                Tag = StateKeys.UiTags.Notify,
                Opacity = 0,
                Child = new TextBlock
                {
                    Text = $"{icon}  {text}",
                    Foreground = fg,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                }
            };

            root.Children.Add(notify);
            _currentNotify = notify;
            _state.Set(StateKeys.Notify.Text, (string?)null);
            _state.Set(StateKeys.Notify.Type, (string?)null);
            _notifyRemainSeconds = 3.0;
            _notifyFadeSeconds = NotifyFadeDuration;
        }
        else if (_notifyRemainSeconds > 0)
        {
            _notifyRemainSeconds -= delta;

            if (_notifyFadeSeconds > 0)
            {
                _notifyFadeSeconds -= delta;
                if (_currentNotify != null)
                {
                    var progress = 1.0 - (_notifyFadeSeconds / NotifyFadeDuration);
                    _currentNotify.Opacity = Math.Clamp(progress, 0, 1);
                }
            }

            if (_notifyRemainSeconds <= 0)
            {
                _notifyFadeSeconds = -NotifyFadeDuration;
                _notifyRemainSeconds = 0;
            }
        }
        else if (_notifyFadeSeconds < 0)
        {
            _notifyFadeSeconds += delta;
            if (_currentNotify != null)
            {
                var progress = Math.Abs(_notifyFadeSeconds) / NotifyFadeDuration;
                _currentNotify.Opacity = Math.Clamp(progress, 0, 1);
            }

            if (_notifyFadeSeconds >= 0)
            {
                if (_outerGrid != null)
                    RemoveNotifyToast(_outerGrid);
                _notifyFadeSeconds = 0;
            }
        }
    }

    private void RemoveNotifyToast(Panel root)
    {
        for (int i = root.Children.Count - 1; i >= 0; i--)
            if (root.Children[i].Tag?.ToString() == StateKeys.UiTags.Notify)
                root.Children.RemoveAt(i);
        _currentNotify = null;
    }

    // ========== 性能 HUD ==========

    private void UpdatePerformanceHud()
    {
        var showHud = _state.Get<bool>(StateKeys.Performance.ShowHud);
        if (!showHud)
        {
            if (_perfHud != null) _perfHud.IsVisible = false;
            return;
        }

        if (_perfHud == null)
        {
            _perfHud = new TextBlock
            {
                FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(220, 0, 255, 0)),
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Padding = new Thickness(6, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                ZIndex = 9999
            };
            _outerGrid?.Children.Add(_perfHud);
        }

        _perfHud.IsVisible = true;
        var fps = _state.Get<double>(StateKeys.Performance.Fps);
        var frameMs = _state.Get<double>(StateKeys.Performance.FrameTimeMs);
        var cmdQueue = _state.Get<int>(StateKeys.Performance.CommandQueueDepth);
        var dslIdx = _state.Get<int>(StateKeys.Performance.DslCurrentIndex);
        var dslTotal = _state.Get<int>(StateKeys.Performance.DslTotalCommands);
        var animCount = _state.Get<int>(StateKeys.Performance.ActiveAnimations);
        var sceneEls = _state.Get<int>(StateKeys.Performance.SceneElementCount);
        var memMb = _state.Get<double>(StateKeys.Performance.MemoryMb);
        var cpCount = _state.Get<int>(StateKeys.Performance.CheckpointCount);

        _perfHud.Text = $"FPS: {fps:F1} | {frameMs:F1}ms\n" +
                        $"Cmd: {cmdQueue} | DSL: {dslIdx}/{dslTotal}\n" +
                        $"Anim: {animCount} | Els: {sceneEls}\n" +
                        $"CP: {cpCount} | Mem: {memMb:F1}MB";
    }

    // ========== 对话模态遮罩 ==========

    private void UpdateDialogMask()
    {
        if (_dialogMask == null) return;
        var waitingType = _state.Get<string>(StateKeys.Dsl.WaitingType) ?? "";
        var isClickable = _state.Get<bool>(StateKeys.Dialog.Clickable);
        _dialogMask.IsVisible = (waitingType == StateKeys.Dsl.WaitingTypes.Dialog
            || waitingType == StateKeys.Dsl.WaitingTypes.WaitSkipable
            || waitingType == StateKeys.Dsl.WaitingTypes.Pause)
            && !isClickable;
    }
}
