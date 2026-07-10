using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Views;

/// <summary>
/// 场景视图——组合根，协调各子模块渲染场景。
/// </summary>
public partial class SceneView : UserControl, ISceneRenderer
{
    private readonly IStateContainer _state;
    private readonly ICommandPipeline _pipeline;
    private readonly II18nService _i18n;
    private readonly ISceneRegistry? _sceneRegistry;
    private readonly IDialogBoxFactory? _dialogBoxFactory;

    // ── 子模块（DI 注入）──
    private readonly IControlFactory _controlFactory;
    private readonly IInteractionBinder _interactionBinder;
    private readonly IOverlayRenderer _overlayRenderer;
    private readonly IVideoPresenter _videoPresenter;
    private readonly IAnimationApplier _animationApplier;

    // ── 对话框 ──
    private DialogBox? _dialogBox;
    private IDialogBox? _dialogBoxIF;

    // ── 状态追踪 ──
    private string _lastSceneName = "";
    private string _lastDialogText = "";
    private string _lastLanguage = "";
    private bool _layoutDirty;
    private string _currentLayoutMode = "grid";
    private Avalonia.Size _lastLayoutSize = new(0, 0);
    private int _varCheckCounter;
    private readonly Dictionary<string, object?> _lastVarValues = new();

    // ── 设计分辨率 + RenderTransform 缩放 ──
    private readonly double _designWidth;
    private readonly double _designHeight;
    private readonly LayoutScaleMode _scaleMode;
    private double _currentLayoutScale = 1.0;

    // ── 视觉树引用 ──
    private Panel? _sceneRoot;
    private Grid? _scaleWrapper;
    private Grid? _outerGrid;
    private Border? _transitionOverlay;
    private Border? _dialogMask;

    public SceneView(
        IStateContainer state,
        ICommandPipeline pipeline,
        II18nService i18n,
        IControlFactory controlFactory,
        IInteractionBinder interactionBinder,
        IOverlayRenderer overlayRenderer,
        IVideoPresenter videoPresenter,
        IAnimationApplier animationApplier,
        ICommandService? cmdService = null,
        ISceneRegistry? sceneRegistry = null,
        IDialogBoxFactory? dialogBoxFactory = null,
        double designWidth = 1920, double designHeight = 1080,
        LayoutScaleMode scaleMode = LayoutScaleMode.Stretch)
    {
        _state = state;
        _pipeline = pipeline;
        _i18n = i18n;
        _controlFactory = controlFactory;
        _interactionBinder = interactionBinder;
        _overlayRenderer = overlayRenderer;
        _videoPresenter = videoPresenter;
        _animationApplier = animationApplier;
        _sceneRegistry = sceneRegistry;
        _dialogBoxFactory = dialogBoxFactory;
        _designWidth = designWidth;
        _designHeight = designHeight;
        _scaleMode = scaleMode;
        Background = Brushes.Transparent;

        SizeChanged += (_, _) => _layoutDirty = true;

        // Keyboard shortcuts (Ren'Py style)
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Space || e.Key == Avalonia.Input.Key.Enter)
            {
                if (_dialogBoxIF != null && !_dialogBoxIF.IsComplete)
                    _dialogBoxIF.SkipToEnd();
                else
                {
                    _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
                    _state.Set(StateKeys.Dialog.Complete, true);
                }
            }
            else if (e.Key == Avalonia.Input.Key.Escape)
            {
                _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
                _state.Set(StateKeys.Dialog.Complete, true);
            }
            else if (e.Key == Avalonia.Input.Key.F5)
                _pipeline.SendAsync(new SaveLoadCommand { SlotId = "quicksave", IsSave = true });
            else if (e.Key == Avalonia.Input.Key.F9)
                _pipeline.SendAsync(new SaveLoadCommand { SlotId = "quicksave", IsSave = false });
        };
    }

    // ========== ISceneRenderer ==========

    public byte[]? CaptureThumbnail(int width = 320, int height = 180)
    {
        try
        {
            var ps = new PixelSize(width, height);
            var dpi = new Vector(96, 96);
            using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(ps, dpi);
            return Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
            {
                bmp.Render(this);
                using var ms = new System.IO.MemoryStream();
                bmp.Save(ms);
                return ms.ToArray();
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SceneView] CaptureThumbnail failed: {ex.Message}"); return null; }
    }

    public void Update(double delta)
    {
        var sceneName = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";
        var dialogText = _state.Get<string>(StateKeys.Dialog.Text) ?? "";

        // === 过渡动画 ===
        UpdateTransition();

        // === 屏幕震动 ===
        UpdateShake();

        // === 语言切换 ===
        var curLang = _state.Get<string>(StateKeys.Scene.CurrentLanguage) ?? "";
        if (curLang != _lastLanguage)
        {
            if (!string.IsNullOrEmpty(curLang)) _i18n.SwitchLanguage(curLang);
            _lastLanguage = curLang;
        }

        // === 场景重建检测 ===
        var elements = _state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        var dirty = _state.Get<bool>(StateKeys.Scene.Dirty);
        if (sceneName != _lastSceneName)
        {
            RebuildScene(sceneName);
            _lastSceneName = sceneName;
        }
        else if (dirty)
        {
            _state.Set(StateKeys.Scene.Dirty, false);
            RebuildScene(sceneName);
            _lastSceneName = sceneName;
        }
        else if (sceneName == _lastSceneName && !string.IsNullOrEmpty(sceneName))
        {
            if (CheckVarChanges())
                _controlFactory.RefreshBoundTextBlocks();
        }

        // === 布局缩放 ===
        if (_scaleWrapper != null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var currentSize = Bounds.Size;
            if (_layoutDirty || currentSize != _lastLayoutSize)
            {
                _lastLayoutSize = currentSize;
                _layoutDirty = false;
                UpdateLayoutScale();
            }
        }

        // === 对话 ===
        if (dialogText != _lastDialogText)
        {
            UpdateDialog(dialogText);
            _lastDialogText = dialogText;
        }
        _dialogBoxIF?.Advance(delta);
        UpdateWindowMode(dialogText);
        UpdateDialogLayout();

        // === 子模块更新 ===
        _overlayRenderer.Update(delta);
        UpdateRuntimeElements();
        _videoPresenter.Update();
        _animationApplier.Apply(_sceneRoot);
    }

    // ========== RebuildScene ==========

    private void RebuildScene(string sceneName)
    {
        _controlFactory.ClearBoundTextBlocks();
        _lastDialogText = "";
        _dialogBoxIF?.Hide();
        _dialogBoxIF?.ResetNvlState();

        // 场景切换时分离子模块
        _overlayRenderer.Detach();
        _videoPresenter.Detach();

        var elements = _state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        if (elements == null || elements.Count == 0)
        {
            _sceneRoot = null;
            _scaleWrapper = null;
            _outerGrid = null;
            _transitionOverlay = null;
            _dialogMask = null;
            Content = new TextBlock { Text = $"场景 [{sceneName}]", Foreground = Brushes.Gray };
            return;
        }

        var sceneEntity = _sceneRegistry?.FindScene(sceneName);
        _currentLayoutMode = sceneEntity?.LayoutMode ?? "grid";

        var parentW = _designWidth;
        var parentH = _designHeight;

        // 根据布局模式创建根容器
        Panel rootPanel;
        Grid? grid = null;

        if (_currentLayoutMode == "canvas")
            rootPanel = new Canvas { Background = Brushes.Black };
        else if (_currentLayoutMode == "stack")
            rootPanel = new StackPanel { Background = Brushes.Black };
        else if (_currentLayoutMode == "panel")
            rootPanel = new Panel { Background = Brushes.Black };
        else
        {
            grid = new Grid { Background = Brushes.Black };
            var gridDef = elements.FirstOrDefault(e =>
                e.ElementType.Equals("grid", StringComparison.OrdinalIgnoreCase));
            if (gridDef != null)
            {
                var cols = gridDef.Properties.GetValueOrDefault("columns")?.ToString();
                var rows = gridDef.Properties.GetValueOrDefault("rows")?.ToString();
                if (cols != null) grid.ColumnDefinitions = ColumnDefinitions.Parse(cols);
                if (rows != null) grid.RowDefinitions = RowDefinitions.Parse(rows);
            }
            rootPanel = grid;
        }

        rootPanel.Width = _designWidth;
        rootPanel.Height = _designHeight;
        rootPanel.HorizontalAlignment = HorizontalAlignment.Center;
        rootPanel.VerticalAlignment = VerticalAlignment.Center;
        _sceneRoot = rootPanel;

        rootPanel.PointerPressed += (_, _) =>
        {
            _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
            _state.Set(StateKeys.Dialog.Complete, true);
        };

        foreach (var element in elements.OrderBy(e => e.Order))
        {
            if (grid != null && element.ElementType.Equals("grid", StringComparison.OrdinalIgnoreCase))
                continue;

            var control = _controlFactory.ConvertToControl(element, parentW, parentH, _currentLayoutMode);
            if (control == null) continue;
            _controlFactory.ApplyLayout(control, element.Properties, parentW, parentH, _currentLayoutMode);
            _interactionBinder.ApplyInteraction(control, element.Properties);
            if (element.Properties.TryGetValue(StateKeys.UiTags.Tag, out var tag) && tag is string s)
                control.Tag = s;
            rootPanel.Children.Add(control);
        }

        // 对话框
        if (_dialogBoxFactory != null)
        {
            _dialogBoxIF = _dialogBoxFactory.Create(_state);
            _dialogBox = null;
        }
        else
        {
            _dialogBox = new DialogBox(_state);
            _dialogBoxIF = _dialogBox;
        }
        var dlgControl = _dialogBoxIF.AsControl();
        dlgControl.SetValue(Grid.ZIndexProperty, 100);
        dlgControl.VerticalAlignment = VerticalAlignment.Bottom;
        dlgControl.HorizontalAlignment = HorizontalAlignment.Stretch;
        ApplyDialogLayout(parentW, parentH);
        if (!string.IsNullOrEmpty(_lastDialogText))
            _dialogBoxIF.SetText(_lastDialogText, _state.Get<string>(StateKeys.Dialog.Speaker));

        // 过渡遮罩
        _transitionOverlay = new Border
        {
            Background = Brushes.Black,
            IsVisible = false,
            ZIndex = 200,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // 对话模态遮罩
        _dialogMask = new Border
        {
            Background = Brushes.Transparent,
            IsVisible = false,
            ZIndex = 50,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _dialogMask.PointerPressed += (_, _) =>
        {
            _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
            _state.Set(StateKeys.Dialog.Complete, true);
        };

        rootPanel.Children.Add(_dialogMask);
        rootPanel.Children.Add(_dialogBoxIF.AsControl());

        // 缩放层
        _scaleWrapper = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Black
        };
        _scaleWrapper.Children.Add(rootPanel);

        // 外层 Grid
        _outerGrid = new Grid { ClipToBounds = true, Background = Brushes.Black };
        _outerGrid.Children.Add(_scaleWrapper);
        _outerGrid.Children.Add(_transitionOverlay);
        Content = _outerGrid;

        // 附加子模块
        _overlayRenderer.Attach(_sceneRoot, _outerGrid, _dialogMask);
        _videoPresenter.Attach(_sceneRoot, _outerGrid);
        _animationApplier.RebuildControlMap(_sceneRoot);

        UpdateLayoutScale();
        _lastLayoutSize = new Size(0, 0);
    }

    // ========== 布局缩放 ==========

    private void UpdateLayoutScale()
    {
        if (_scaleWrapper == null) return;
        var actualW = Bounds.Width > 0 ? Bounds.Width : _designWidth;
        var actualH = Bounds.Height > 0 ? Bounds.Height : _designHeight;
        var scaleX = actualW / _designWidth;
        var scaleY = actualH / _designHeight;

        double finalScaleX, finalScaleY;
        switch (_scaleMode)
        {
            case LayoutScaleMode.Contain:
                finalScaleX = finalScaleY = Math.Min(scaleX, scaleY);
                break;
            case LayoutScaleMode.Cover:
                finalScaleX = finalScaleY = Math.Max(scaleX, scaleY);
                break;
            default: // Stretch
                finalScaleX = scaleX;
                finalScaleY = scaleY;
                break;
        }

        _currentLayoutScale = finalScaleX;
        _scaleWrapper.RenderTransform = new ScaleTransform(finalScaleX, finalScaleY);
        _scaleWrapper.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
    }

    // ========== 过渡 + 震动 ==========

    private void UpdateTransition()
    {
        var transActive = _state.Get<bool>(StateKeys.Transition.Active);
        var transProgress = _state.Get<double>(StateKeys.Transition.Progress);
        if (_transitionOverlay != null)
        {
            if (transActive && transProgress < 1.0 && transProgress > 0.0)
            {
                _transitionOverlay.IsVisible = true;
                _transitionOverlay.Background = new SolidColorBrush(
                    Color.FromArgb((byte)((1.0 - transProgress) * 255), 0, 0, 0));
            }
            else _transitionOverlay.IsVisible = false;

            var offsetX = _state.Get<double>(StateKeys.Transition.OffsetX);
            var offsetY = _state.Get<double>(StateKeys.Transition.OffsetY);
            var scale = _state.Get<double>(StateKeys.Transition.Scale);
            if (_sceneRoot != null && (offsetX != 0 || offsetY != 0 || scale != 1.0))
            {
                var transform = new TransformGroup();
                if (offsetX != 0 || offsetY != 0)
                    transform.Children.Add(new TranslateTransform(offsetX, offsetY));
                if (scale != 1.0 && scale > 0)
                    transform.Children.Add(new ScaleTransform(scale, scale));
                _sceneRoot.RenderTransform = transform;
            }
            else if (_sceneRoot != null && transActive)
            {
                _sceneRoot.RenderTransform = null;
            }
        }
    }

    private void UpdateShake()
    {
        var shakeActive = _state.Get<bool>(StateKeys.Shake.Active);
        var shakeOffsetX = _state.Get<double>(StateKeys.Shake.OffsetX);
        var shakeOffsetY = _state.Get<double>(StateKeys.Shake.OffsetY);
        var transActive = _state.Get<bool>(StateKeys.Transition.Active);
        if (_sceneRoot != null)
        {
            if (shakeActive && (shakeOffsetX != 0 || shakeOffsetY != 0))
            {
                var existingTransform = _sceneRoot.RenderTransform as TransformGroup;
                var shakeTransform = new TransformGroup();
                if (existingTransform != null)
                {
                    foreach (var child in existingTransform.Children)
                        shakeTransform.Children.Add(child);
                }
                shakeTransform.Children.Add(new TranslateTransform(shakeOffsetX, shakeOffsetY));
                _sceneRoot.RenderTransform = shakeTransform;
            }
            else if (!shakeActive && !transActive)
            {
                _sceneRoot.RenderTransform = null;
            }
        }
    }

    // ========== 对话框 ==========

    private void UpdateDialog(string text)
    {
        if (string.IsNullOrEmpty(text)) _dialogBoxIF?.Hide();
        else
        {
            var speaker = _state.Get<string>(StateKeys.Dialog.Speaker);
            _dialogBoxIF?.SetText(text, speaker);
        }
    }

    private void UpdateWindowMode(string dialogText)
    {
        if (_dialogBoxIF == null) return;
        var mode = _state.Get<string>(StateKeys.Dialog.WindowMode) ?? "auto";
        var dlgCtrl = _dialogBoxIF.AsControl();
        switch (mode)
        {
            case "show": dlgCtrl.IsVisible = true; break;
            case "hide": dlgCtrl.IsVisible = false; break;
            default: dlgCtrl.IsVisible = !string.IsNullOrEmpty(dialogText); break;
        }
    }

    private void UpdateDialogLayout()
    {
        ApplyDialogLayout(_designWidth, _designHeight);
    }

    private void ApplyDialogLayout(double parentW, double parentH)
    {
        var w = _state.Get<double?>(StateKeys.Dialog.WidthPercent) ?? _state.Get<double?>(StateKeys.Dialog.WidthPercentDefault);
        var h = _state.Get<double?>(StateKeys.Dialog.HeightPercent) ?? _state.Get<double?>(StateKeys.Dialog.HeightPercentDefault);
        var ml = _state.Get<double?>(StateKeys.Dialog.MarginLeft) ?? _state.Get<double?>(StateKeys.Dialog.MarginLeftDefault);
        var mb = _state.Get<double?>(StateKeys.Dialog.MarginBottom) ?? _state.Get<double?>(StateKeys.Dialog.MarginBottomDefault);

        if (_dialogBoxIF == null) return;

        var dlgCtrl = _dialogBoxIF.AsControl();
        dlgCtrl.Width = w.HasValue ? parentW * w.Value / 100.0 : double.NaN;
        dlgCtrl.HorizontalAlignment = HorizontalAlignment.Stretch;
        dlgCtrl.MaxHeight = h.HasValue ? parentH * h.Value / 100.0 : parentH * 0.35;
        dlgCtrl.VerticalAlignment = VerticalAlignment.Bottom;
        dlgCtrl.Margin = new Thickness(ml ?? 0, 0, 0, mb ?? 0);
    }

    // ========== 运行时元素 ==========

    private void UpdateRuntimeElements()
    {
        var rt = _state.Get<object>(StateKeys.Scene.RuntimeElements);
        if (rt is not List<UIElementEntity> elements) return;
        var root = _sceneRoot;
        if (root == null) return;
        for (int i = root.Children.Count - 1; i >= 0; i--)
            if (root.Children[i] is Control c && c.Tag?.ToString() == StateKeys.UiTags.Runtime)
                root.Children.RemoveAt(i);
        var parentW = _designWidth;
        var parentH = _designHeight;
        foreach (var el in elements)
        {
            var ctrl = _controlFactory.ConvertToControl(el, parentW, parentH, _currentLayoutMode);
            if (ctrl == null) continue;
            ctrl.Tag = el.Properties.TryGetValue(StateKeys.UiTags.Tag, out var tagVal) ? tagVal?.ToString() : StateKeys.UiTags.Runtime;
            ctrl.SetValue(Grid.ZIndexProperty, 50);
            _controlFactory.ApplyLayout(ctrl, el.Properties, parentW, parentH, _currentLayoutMode);
            _interactionBinder.ApplyInteraction(ctrl, el.Properties);
            root.Children.Add(ctrl);
        }
    }

    // ========== 变量变化检测 ==========

    private bool CheckVarChanges()
    {
        _varCheckCounter++;
        if (_varCheckCounter % 5 != 0) return false;
        bool changed = false;
        foreach (var key in _state.Keys)
        {
            if (key is string sk && !sk.StartsWith(StateKeys.SystemPrefix))
            {
                var val = _state.Get<object>(sk);
                if (!_lastVarValues.TryGetValue(sk, out var last) || !Equals(val, last))
                {
                    _lastVarValues[sk] = val;
                    changed = true;
                }
            }
        }
        return changed;
    }
}
