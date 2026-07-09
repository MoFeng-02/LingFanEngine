using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Views;

public partial class SceneView : UserControl
{
    private readonly IStateContainer _state;
    private readonly ICommandPipeline _pipeline;
    private readonly II18nService _i18n;
    private readonly ICommandService? _cmdService;
    private readonly ISceneRegistry? _sceneRegistry;
    private readonly IDialogBoxFactory? _dialogBoxFactory;
    private string _lastSceneName = "";
    private string _lastDialogText = "";
    private string _lastLanguage = "";
    private readonly List<(TextBlock tb, string rawText)> _boundTextBlocks = new();
    private Views.DialogBox? _dialogBox;
    private IDialogBox? _dialogBoxIF;
    private Border? _transitionOverlay;
    /// <summary>对话模态遮罩——say 期间透明覆盖场景元素，拦截点击推进对话</summary>
    private Border? _dialogMask;
    private Panel? _menuPanel;
    private Panel? _inputPanel;
    private TextBox? _inputBox;

    // ── 布局追踪（Phase 27：不再用于 resize 重布局，保留供动画系统查找）──
    private bool _layoutDirty;
    private string _currentLayoutMode = "grid";
    private readonly List<(Control control, Dictionary<string, object> props)> _trackedControls = new();
    /// <summary>上次布局时使用的 Bounds 尺寸——用于检测 Bounds 变化触发 UpdateLayoutScale</summary>
    private Avalonia.Size _lastLayoutSize = new(0, 0);

    private int _varCheckCounter;
    private double _notifyRemainSeconds;
    private double _notifyFadeSeconds;
    private const double NotifyFadeDuration = 0.3;
    private Avalonia.Controls.Control? _currentNotify;
    private readonly Dictionary<string, object?> _lastVarValues = new();
    private TextBlock? _perfHud;

    // ── 视频播放器（GpuMediaPlayer 控件引用）──
    private MediaPlayer.Controls.GpuMediaPlayer? _videoPlayer;
    private string _lastVideoPath = "";
    private bool _lastVideoIsPlaying;
    private double _lastVideoPosition = -1;
    private double _lastVideoDuration = -1;
    private bool _lastVideoFinished;

    // ── 过场动画遮罩 ──
    private Border? _cutsceneMask;

    // ── 设计分辨率 + RenderTransform 缩放（Phase 27）──
    // 场景内容固定在设计分辨率内布局，通过 _scaleWrapper 的 RenderTransform 整体缩放到实际窗口
    // 对标 Ren'Py 虚拟分辨率机制——GPU 渲染层变换，O(1) 缩放，零布局 pass
    private readonly double _designWidth;
    private readonly double _designHeight;
    private readonly LayoutScaleMode _scaleMode;
    /// <summary>场景根容器（所有场景元素 + 对话框 + 遮罩），RenderTransform 用于过渡/震动</summary>
    private Panel? _sceneRoot;
    /// <summary>缩放包装层，RenderTransform 用于布局缩放（窗口适配）</summary>
    private Grid? _scaleWrapper;
    /// <summary>最外层 Grid，包含缩放层 + 不缩放的覆盖层（过渡遮罩/性能HUD/通知/过场遮罩）</summary>
    private Grid? _outerGrid;
    private double _currentLayoutScale = 1.0;

    public SceneView(IStateContainer state, ICommandPipeline pipeline,
        II18nService i18n,
        ICommandService? cmdService = null,
        ISceneRegistry? sceneRegistry = null,
        IDialogBoxFactory? dialogBoxFactory = null,
        double designWidth = 1920, double designHeight = 1080,
        LayoutScaleMode scaleMode = LayoutScaleMode.Stretch)
    {
        _state = state;
        _pipeline = pipeline;
        _i18n = i18n;
        _cmdService = cmdService;
        _sceneRegistry = sceneRegistry;
        _dialogBoxFactory = dialogBoxFactory;
        _designWidth = designWidth;
        _designHeight = designHeight;
        _scaleMode = scaleMode;
        Background = Brushes.Transparent;
        // 预渲染占位 Grid：带标题的启动画面。首帧 Avalonia 渲染管线冷启动期间（~500ms-1s）
        // 用户看到的是这个占位 Grid 而非黑屏，ReplacedScene 后无缝替换
        var splash = new Grid { Background = Brushes.Black };
        splash.Children.Add(new TextBlock
        {
            Text = "灵泛引擎",
            Foreground = new SolidColorBrush(Color.FromArgb(80, 80, 80, 80)),
            FontSize = 28,
            FontFamily = new Avalonia.Media.FontFamily("Microsoft YaHei"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        Content = splash;
        // 窗口缩放时标记需要重布局
        SizeChanged += (_, _) => _layoutDirty = true;
        // Keyboard shortcuts (Ren'Py style)
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Space || e.Key == Avalonia.Input.Key.Enter)
            {
                // Ren'Py 行为：先 skip-to-end（打字机运行中），再 complete（已在末尾）
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
                // Escape = 跳到对话末尾（不导航，只推进对话）
                _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
                _state.Set(StateKeys.Dialog.Complete, true);
            }
            else if (e.Key == Avalonia.Input.Key.F5)
                _pipeline.SendAsync(new SaveLoadCommand { SlotId = "quicksave", IsSave = true });
            else if (e.Key == Avalonia.Input.Key.F9)
                _pipeline.SendAsync(new SaveLoadCommand { SlotId = "quicksave", IsSave = false });
        };
    }

    public byte[]? CaptureThumbnail(int width = 320, int height = 180)
{
    try
    {
        var ps = new Avalonia.PixelSize(width, height);
        var dpi = new Avalonia.Vector(96, 96);
        using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(ps, dpi);
        // Render 必须在 UI 线程执行
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

        var transActive = _state.Get<bool>(StateKeys.Transition.Active);
        var transProgress = _state.Get<double>(StateKeys.Transition.Progress);
        if (_transitionOverlay != null)
        {
            if (transActive && transProgress < 1.0 && transProgress > 0.0)
            {
                _transitionOverlay.IsVisible = true;
                _transitionOverlay.Background = new SolidColorBrush(Color.FromArgb((byte)((1.0 - transProgress) * 255), 0, 0, 0));
            }
            else _transitionOverlay.IsVisible = false;
            // 应用过渡偏移/缩放到场景内容
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

        // 屏幕震动效果——在过渡之上叠加震动偏移
        var shakeActive = _state.Get<bool>(StateKeys.Shake.Active);
        var shakeOffsetX = _state.Get<double>(StateKeys.Shake.OffsetX);
        var shakeOffsetY = _state.Get<double>(StateKeys.Shake.OffsetY);
        if (_sceneRoot != null)
        {
            if (shakeActive && (shakeOffsetX != 0 || shakeOffsetY != 0))
            {
                // 叠加震动偏移到现有变换之上
                var existingTransform = _sceneRoot.RenderTransform as TransformGroup;
                var shakeTransform = new TransformGroup();
                // 保留过渡变换
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
                // 无震动无过渡时清除变换
                _sceneRoot.RenderTransform = null;
            }
        }

        var curLang = _state.Get<string>(StateKeys.Scene.CurrentLanguage) ?? "";
        if (curLang != _lastLanguage)
        {
            if (!string.IsNullOrEmpty(curLang)) _i18n.SwitchLanguage(curLang);
            _lastLanguage = curLang;
        }

        // 场景切换 / 脏标记才重建；窗口 resize 由 Avalonia 原生布局处理，不重建
        var elements = _state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        var dirty = _state.Get<bool>(StateKeys.Scene.Dirty);
        if (sceneName != _lastSceneName)
        {
            System.Diagnostics.Debug.WriteLine($"[SceneView] Rebuild: sceneName changed '{_lastSceneName}' -> '{sceneName}', elements.count={elements?.Count ?? 0}");
            RebuildScene(sceneName);
            _lastSceneName = sceneName;
        }
        else if (dirty)
        {
            _state.Set(StateKeys.Scene.Dirty, false);
            System.Diagnostics.Debug.WriteLine($"[SceneView] Rebuild: Dirty=true, scene='{sceneName}', elements.count={elements?.Count ?? 0}");
            RebuildScene(sceneName);
            _lastSceneName = sceneName;
        }
        else if (sceneName == _lastSceneName && !string.IsNullOrEmpty(sceneName))
        {
            if (CheckVarChanges()) RefreshBoundTextBlocks();
        }

        // 窗口缩放：只更新 _scaleWrapper 的 RenderTransform（O(1) 属性赋值，GPU 自动缩放）
        // 不依赖 SizeChanged 一次性事件——启动时 SizeChanged 可能在 RebuildScene 之前
        // 触发并被清掉，导致首帧布局用错误的 Bounds。改为每帧检测 Bounds 实际变化。
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

        RefreshBoundTextBlocks();

        if (dialogText != _lastDialogText)
        {
            UpdateDialog(dialogText);
            _lastDialogText = dialogText;
        }
        _dialogBoxIF?.Advance(delta);

        // Phase 24: window 窗口模式控制对话框可见性
        UpdateWindowMode(dialogText);

        UpdateDialogLayout();
        UpdateMenuOverlay();
        UpdateInputOverlay();
        UpdateDialogMask();
        UpdateRuntimeElements();
        UpdateVideoPlayer();
        UpdateNotifyToast();
        ApplyAnimations();
        UpdatePerformanceHud();
    }

    private void RebuildScene(string sceneName)
    {
        _boundTextBlocks.Clear();
        _trackedControls.Clear();
        _lastDialogText = ""; // 清除旧对话防止场景切换后残留在新 DialogBox
        _dialogBoxIF?.Hide();

        // 场景切换时停止视频播放并从视觉树移除
        RemoveVideoPlayerFromTree();
        // 清理过场遮罩引用（Content 被替换后旧 mask 已失效）
        if (_cutsceneMask != null)
        {
            _cutsceneMask.IsVisible = false;
            _cutsceneMask = null;
        }
        var elements = _state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        if (elements == null || elements.Count == 0)
        {
            // Phase 27：清空旧引用，防止后续 Update 调用将覆盖层添加到已脱离视觉树的死面板
            _sceneRoot = null;
            _scaleWrapper = null;
            _outerGrid = null;
            _transitionOverlay = null;
            _dialogMask = null;
            Content = new TextBlock { Text = $"场景 [{sceneName}]", Foreground = Brushes.Gray };
            return;
        }

        // 读取场景布局模式（从 SceneEntity 获取，默认 grid）
        var sceneEntity = _sceneRegistry?.FindScene(sceneName);
        _currentLayoutMode = sceneEntity?.LayoutMode ?? "grid";

        // Phase 27：布局永远基于设计分辨率，不再用 Bounds.Width/Height
        var parentW = _designWidth;
        var parentH = _designHeight;

        // 根据布局模式创建根容器
        Panel rootPanel;
        Grid? grid = null;

        if (_currentLayoutMode == "canvas")
        {
            rootPanel = new Canvas { Background = Brushes.Black };
        }
        else if (_currentLayoutMode == "stack")
        {
            rootPanel = new StackPanel { Background = Brushes.Black };
        }
        else if (_currentLayoutMode == "panel")
        {
            rootPanel = new Panel { Background = Brushes.Black };
        }
        else
        {
            // 默认 Grid
            grid = new Grid { Background = Brushes.Black };

            // 如果第一个元素是 grid 类型且带 columns/rows 定义，使用它的定义
            var gridDef = elements.FirstOrDefault(e =>
                e.ElementType.Equals("grid", StringComparison.OrdinalIgnoreCase));
            if (gridDef != null)
            {
                var cols = gridDef.Properties.GetValueOrDefault("columns")?.ToString();
                var rows = gridDef.Properties.GetValueOrDefault("rows")?.ToString();
                if (cols != null)
                    grid.ColumnDefinitions = ColumnDefinitions.Parse(cols);
                if (rows != null)
                    grid.RowDefinitions = RowDefinitions.Parse(rows);
            }
            rootPanel = grid;
        }

        // Phase 27：场景根容器固定为设计分辨率，居中放置
        rootPanel.Width = _designWidth;
        rootPanel.Height = _designHeight;
        rootPanel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        rootPanel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        _sceneRoot = rootPanel;

        rootPanel.PointerPressed += (_, _) =>
        {
            // 任意点击推进对话/结束硬暂停（对标 Ren'Py click to advance）
            // 注意：当遮罩可见时，遮罩会拦截点击，此回调不会被触发
            // 此回调仅在遮罩不可见时（idle/clickable=true）生效
            _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
            _state.Set(StateKeys.Dialog.Complete, true);
        };

        foreach (var element in elements.OrderBy(e => e.Order))
        {
            // 跳过 grid 定义元素本身（它只提供 columns/rows 定义，不渲染为控件）
            if (grid != null && element.ElementType.Equals("grid", StringComparison.OrdinalIgnoreCase))
                continue;

            var control = ConvertToControl(element, parentW, parentH, _currentLayoutMode);
            if (control == null) continue;
            ApplyLayout(control, element.Properties, parentW, parentH, _currentLayoutMode);
            // 通用交互：nav/cmd/hover/selected/disabled（任何控件类型均可）
            ApplyInteraction(control, element.Properties);
            // 设置 Tag 用于动画匹配（AnimateCommand 通过 Tag 找到目标控件）
            if (element.Properties.TryGetValue(StateKeys.UiTags.Tag, out var tag) && tag is string s)
                control.Tag = s;
            // 追踪控件（Phase 27：保留供潜在的未来用途，resize 不再需要重算）
            _trackedControls.Add((control, element.Properties));
            rootPanel.Children.Add(control);
        }

        // 通过工厂创建对话框（支持游戏自定义），回退到内置 DialogBox
        if (_dialogBoxFactory != null)
        {
            _dialogBoxIF = _dialogBoxFactory.Create(_state);
            _dialogBox = null;
            var dlgControl = _dialogBoxIF.AsControl();
            dlgControl.SetValue(Grid.ZIndexProperty, 100);
            dlgControl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            dlgControl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }
        else
        {
            _dialogBox = new Views.DialogBox(_state);
            _dialogBoxIF = _dialogBox;
            _dialogBox.SetValue(Grid.ZIndexProperty, 100);
            _dialogBox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            _dialogBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }
        ApplyDialogLayout(parentW, parentH);
        if (!string.IsNullOrEmpty(_lastDialogText))
            _dialogBoxIF.SetText(_lastDialogText, _state.Get<string>(StateKeys.Dialog.Speaker));

        _transitionOverlay = new Border
        {
            Background = Brushes.Black,
            IsVisible = false,
            ZIndex = 200, // 在缩放层之上，全屏不缩放
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        // 对话模态遮罩——透明覆盖场景元素，拦截点击推进对话
        // ZIndex=50：在场景元素(0)之上、对话框(100)之下
        // 仅在 say 等待中且 Clickable=false 时显示
        _dialogMask = new Border
        {
            Background = Brushes.Transparent,
            IsVisible = false,
            ZIndex = 50,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        _dialogMask.PointerPressed += (_, _) =>
        {
            // 遮罩拦截点击——推进对话而非触发按钮
            _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
            _state.Set(StateKeys.Dialog.Complete, true);
        };

        // Phase 27：两层架构——_sceneRoot 在缩放层内，覆盖层在缩放层外
        rootPanel.Children.Add(_dialogMask);
        if (_dialogBoxIF != null) rootPanel.Children.Add(_dialogBoxIF.AsControl());

        // _scaleWrapper 包裹场景根容器，承载布局缩放 RenderTransform
        _scaleWrapper = new Grid
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            Background = Brushes.Black
        };
        _scaleWrapper.Children.Add(rootPanel);

        // _outerGrid 包含缩放层 + 不缩放的覆盖层
        // Phase 27：_outerGrid 设黑色背景——RenderTransform 缩放 _scaleWrapper 时，
        // 缩放只影响渲染输出，_scaleWrapper 的黑色背景也被缩放，黑边区域需要 _outerGrid 兜底
        _outerGrid = new Grid { ClipToBounds = true, Background = Brushes.Black };
        _outerGrid.Children.Add(_scaleWrapper);
        _outerGrid.Children.Add(_transitionOverlay);
        Content = _outerGrid;

        // 首帧计算缩放
        UpdateLayoutScale();

        // 重置布局尺寸追踪——确保下一帧 Update 检测到 Bounds 变化时自动重算
        _lastLayoutSize = new Avalonia.Size(0, 0);
    }

    /// <summary>
    /// Phase 27：计算并应用布局缩放——O(1) 属性赋值，GPU 自动处理缩放绘制
    /// <para>三种模式：Contain（留黑边）/ Cover（裁边缘）/ Stretch（填满窗口，默认）</para>
    /// </summary>
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
                // 等比缩放，短边对齐，留黑边
                finalScaleX = finalScaleY = Math.Min(scaleX, scaleY);
                break;
            case LayoutScaleMode.Cover:
                // 等比缩放，长边对齐，裁边缘
                finalScaleX = finalScaleY = Math.Max(scaleX, scaleY);
                break;
            default: // Stretch
                // 独立 X/Y 缩放，填满窗口，不黑边不裁切
                finalScaleX = scaleX;
                finalScaleY = scaleY;
                break;
        }

        _currentLayoutScale = finalScaleX;
        _scaleWrapper.RenderTransform = new ScaleTransform(finalScaleX, finalScaleY);
        _scaleWrapper.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
    }

    // ========== 统一布局 ==========

    /// <summary>
    /// 统一映射到 Avalonia 原生布局属性
    /// <para>百分比精确定位：x=25% → Margin.Left = parentW * 0.25（parentW = 设计分辨率宽度，不随窗口变化）</para>
    /// <para>百分比尺寸：width=50% → control.Width = parentW * 0.5</para>
    /// <para>Canvas 模式：x/y → Canvas.SetLeft/Top</para>
    /// <para>锚点系统：xanchor=0.5 → 控件中心对齐到 xpos</para>
    /// <para>偏移系统：xoffset=10 → 在 xpos 计算结果上额外加 10 像素</para>
    /// <para>  col/row     → Grid.SetColumn / Grid.SetRow（Avalonia 原生）</para>
    /// </summary>
    private static void ApplyLayout(Control control, Dictionary<string, object> props, double pw, double ph,
        string layoutMode = "grid")
    {
        // === Grid 附着属性（Avalonia 原生，最优先）===
        var col = LayoutHelper.ParseInt(props, "col");
        if (col.HasValue) Grid.SetColumn(control, col.Value);
        var row = LayoutHelper.ParseInt(props, "row");
        if (row.HasValue) Grid.SetRow(control, row.Value);
        var colspan = LayoutHelper.ParseInt(props, "colspan");
        if (colspan.HasValue) Grid.SetColumnSpan(control, colspan.Value);
        var rowspan = LayoutHelper.ParseInt(props, "rowspan");
        if (rowspan.HasValue) Grid.SetRowSpan(control, rowspan.Value);

        // === 尺寸：Width / Height（支持百分比）===
        var widthStr = props.GetValueOrDefault("width")?.ToString();
        var heightStr = props.GetValueOrDefault("height")?.ToString();

        if (widthStr != null)
        {
            if (widthStr.TrimEnd('%') == "100" || widthStr == "*")
            {
                // 100% → Stretch，不设 Width
                if (control.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Stretch)
                    { }
                else if (!props.ContainsKey("halign") && !props.ContainsKey("align") && !props.ContainsKey("xalign"))
                    control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            }
            else if (widthStr.EndsWith('%'))
            {
                // 百分比宽度 → 精确像素
                var resolvedW = LayoutHelper.ResolvePercentSize(props.GetValueOrDefault("width"), pw);
                if (!double.IsNaN(resolvedW) && resolvedW > 0)
                    control.Width = resolvedW;
            }
            else
            {
                var w = LayoutHelper.ParseDouble(props, "width");
                if (w > 0) control.Width = w;
            }
        }

        if (heightStr != null)
        {
            if (heightStr.TrimEnd('%') == "100" || heightStr == "*")
            {
                if (!props.ContainsKey("valign") && !props.ContainsKey("yalign"))
                    control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            }
            else if (heightStr.EndsWith('%'))
            {
                // 百分比高度 → 精确像素
                var resolvedH = LayoutHelper.ResolvePercentSize(props.GetValueOrDefault("height"), ph);
                if (!double.IsNaN(resolvedH) && resolvedH > 0)
                    control.Height = resolvedH;
            }
            else
            {
                var h = LayoutHelper.ParseDouble(props, "height");
                if (h > 0) control.Height = h;
            }
        }

        // MinWidth / MinHeight / MaxWidth / MaxHeight（支持百分比）
        var minWVal = props.GetValueOrDefault("minWidth");
        if (minWVal != null)
        {
            var minW = LayoutHelper.ResolvePercentSize(minWVal, pw);
            if (!double.IsNaN(minW) && minW > 0) control.MinWidth = minW;
        }
        var minHVal = props.GetValueOrDefault("minHeight");
        if (minHVal != null)
        {
            var minH = LayoutHelper.ResolvePercentSize(minHVal, ph);
            if (!double.IsNaN(minH) && minH > 0) control.MinHeight = minH;
        }
        var maxWVal = props.GetValueOrDefault("maxWidth");
        if (maxWVal != null)
        {
            var maxW = LayoutHelper.ResolvePercentSize(maxWVal, pw);
            if (!double.IsNaN(maxW) && maxW > 0) control.MaxWidth = maxW;
        }
        var maxHVal = props.GetValueOrDefault("maxHeight");
        if (maxHVal != null)
        {
            var maxH = LayoutHelper.ResolvePercentSize(maxHVal, ph);
            if (!double.IsNaN(maxH) && maxH > 0) control.MaxHeight = maxH;
        }

        // === Canvas 绝对定位模式 ===
        if (layoutMode == "canvas")
        {
            var xVal = props.GetValueOrDefault("x");
            var yVal = props.GetValueOrDefault("y");
            var xoffset = LayoutHelper.ParseDouble(props, "xoffset");
            var yoffset = LayoutHelper.ParseDouble(props, "yoffset");

            if (xVal != null)
            {
                var xPx = LayoutHelper.ResolvePercentPosition(xVal, pw) + xoffset;
                // 锚点偏移：xanchor=0.5 → 减去控件宽度的一半
                var xanchor = LayoutHelper.ParseDouble(props, "xanchor");
                if (xanchor > 0 && !double.IsNaN(control.Width))
                    xPx -= control.Width * xanchor;
                Canvas.SetLeft(control, xPx);
            }
            if (yVal != null)
            {
                var yPx = LayoutHelper.ResolvePercentPosition(yVal, ph) + yoffset;
                var yanchor = LayoutHelper.ParseDouble(props, "yanchor");
                if (yanchor > 0 && !double.IsNaN(control.Height))
                    yPx -= control.Height * yanchor;
                Canvas.SetTop(control, yPx);
            }

            // Canvas 模式下也应用非定位属性
            ApplyCommonProps(control, props);
            return;
        }

        // === Grid/Panel/Stack 相对定位模式 ===

        // 对齐：HorizontalAlignment / VerticalAlignment
        // 支持 halign/align/xalign 和 valign/yalign
        var halignKey = props.ContainsKey("halign") ? "halign"
            : props.ContainsKey("align") ? "align"
            : props.ContainsKey("xalign") ? "xalign" : null;

        // 如果有 x 百分比且未显式设 halign，不自动推断对齐——改为用 Margin 精确定位
        // 只有在没有 x 属性时才看 xalign 中的 align 值
        if (halignKey != null)
        {
            control.HorizontalAlignment = props[halignKey]?.ToString()?.ToLowerInvariant() switch
            {
                "center" => Avalonia.Layout.HorizontalAlignment.Center,
                "right" => Avalonia.Layout.HorizontalAlignment.Right,
                "stretch" => Avalonia.Layout.HorizontalAlignment.Stretch,
                _ => Avalonia.Layout.HorizontalAlignment.Left
            };
        }

        string? valignKey = null;
        if (props.ContainsKey("valign"))
            valignKey = "valign";
        else if (props.ContainsKey("yalign"))
            valignKey = "yalign";

        if (valignKey != null)
        {
            control.VerticalAlignment = props[valignKey]?.ToString()?.ToLowerInvariant() switch
            {
                "center" => Avalonia.Layout.VerticalAlignment.Center,
                "bottom" => Avalonia.Layout.VerticalAlignment.Bottom,
                "stretch" => Avalonia.Layout.VerticalAlignment.Stretch,
                _ => Avalonia.Layout.VerticalAlignment.Top
            };
        }

        // === 位置：百分比/像素 → Margin ===
        // 百分比 x/y → 精确像素 Margin（窗口缩放时重算）
        // 固定像素 x/y → 直接 Margin
        //
        // halign 与 x 的交互语义（与 Ren'Py xpos+xanchor 一致）：
        //   halign=left（默认） → x = 左边缘位置   → marginLeft = xPx
        //   halign=center        → x = 中心位置     → marginLeft = 2*xPx - pw（利用 Center 对齐特性）
        //   halign=right         → x = 右边缘位置   → marginRight = pw - xPx
        // 同理 valign 与 y 的交互。
        //
        // 显式 xanchor 优先：xanchor=0.5 等价于 halign=center，但需要已知 control.Width。
        var marginStr = props.GetValueOrDefault("margin")?.ToString();
        if (marginStr != null)
        {
            control.Margin = LayoutHelper.ParseThickness(marginStr);
        }
        else
        {
            var xVal = props.GetValueOrDefault("x");
            var yVal = props.GetValueOrDefault("y");
            var xoffset = LayoutHelper.ParseDouble(props, "xoffset");
            var yoffset = LayoutHelper.ParseDouble(props, "yoffset");

            double marginLeft = 0, marginTop = 0;
            double marginRight = 0, marginBottom = 0;
            bool hasX = false, hasY = false, hasRight = false, hasBottom = false;

            if (xVal != null)
            {
                var xPx = LayoutHelper.ResolvePercentPosition(xVal, pw) + xoffset;
                hasX = true;

                // 显式锚点优先（需已知控件宽度）
                var xanchor = LayoutHelper.ParseDouble(props, "xanchor");

                if (xanchor > 0 && !double.IsNaN(control.Width))
                {
                    // 锚点定位：marginLeft = xPx - width * anchor
                    marginLeft = xPx - control.Width * xanchor;
                }
                else if (control.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Center)
                {
                    // halign=center：x 表示中心位置
                    // 公式推导：Center 对齐时可用空间中心 = marginLeft/2 + pw/2
                    // 令其等于 xPx → marginLeft = 2*xPx - pw
                    marginLeft = 2 * xPx - pw;
                }
                else if (control.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right)
                {
                    // halign=right：x 表示右边缘位置
                    marginRight = pw - xPx;
                    hasRight = true;
                }
                else
                {
                    // halign=left/stretch/默认：x 表示左边缘位置
                    marginLeft = xPx;
                }
            }

            if (yVal != null)
            {
                var yPx = LayoutHelper.ResolvePercentPosition(yVal, ph) + yoffset;
                hasY = true;

                var yanchor = LayoutHelper.ParseDouble(props, "yanchor");

                if (yanchor > 0 && !double.IsNaN(control.Height))
                {
                    marginTop = yPx - control.Height * yanchor;
                }
                else if (control.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Center)
                {
                    // valign=center：y 表示中心位置
                    marginTop = 2 * yPx - ph;
                }
                else if (control.VerticalAlignment == Avalonia.Layout.VerticalAlignment.Bottom)
                {
                    // valign=bottom：y 表示底边缘位置
                    marginBottom = ph - yPx;
                    hasBottom = true;
                }
                else
                {
                    // valign=top/stretch/默认：y 表示顶边缘位置
                    marginTop = yPx;
                }
            }

            // right/bottom 属性独立设置（优先级高于 halign+x 推导的 marginRight）
            var rightVal = props.GetValueOrDefault("right");
            var bottomVal = props.GetValueOrDefault("bottom");

            if (rightVal != null)
            {
                marginRight = LayoutHelper.ResolvePercentPosition(rightVal, pw);
                hasRight = true;
            }
            if (bottomVal != null)
            {
                marginBottom = LayoutHelper.ResolvePercentPosition(bottomVal, ph);
                hasBottom = true;
            }

            if (hasX || hasY || hasRight || hasBottom)
                control.Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom);
        }

        // 通用属性（padding/opacity/visible/enabled/zindex/clip/cursor/transform/border/stack）
        ApplyCommonProps(control, props);
    }

    /// <summary>
    /// 应用通用属性（非定位类）——从 ApplyLayout 抽取，供 Canvas 和 Grid 模式共用
    /// </summary>
    private static void ApplyCommonProps(Control control, Dictionary<string, object> props)
    {
        // === 内边距：Padding ===
        var paddingStr = props.GetValueOrDefault("padding")?.ToString();
        if (paddingStr != null)
        {
            var pad = LayoutHelper.ParseThickness(paddingStr);
            switch (control)
            {
                case TextBlock tb: tb.Padding = pad; break;
                case Button bt: bt.Padding = pad; break;
                case Border bd: bd.Padding = pad; break;
                case TemplatedControl tc: tc.Padding = pad; break;
            }
        }

        // === 透明度：Opacity ===
        var opacity = LayoutHelper.ParseDouble(props, "opacity");
        if (opacity >= 0) control.Opacity = Math.Clamp(opacity, 0, 1);

        // === 可见性：IsVisible ===
        if (props.TryGetValue("visible", out var visVal))
        {
            control.IsVisible = visVal switch
            {
                bool b => b,
                string s => !s.Equals("false", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        // === 可用性：IsEnabled ===
        if (props.TryGetValue("enabled", out var enVal))
        {
            control.IsEnabled = enVal switch
            {
                bool b => b,
                string s => !s.Equals("false", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        // === 层级：ZIndex ===
        var zindex = LayoutHelper.ParseInt(props, "zindex");
        if (zindex.HasValue) control.SetValue(ZIndexProperty, zindex.Value);

        // === 裁剪：ClipToBounds ===
        if (props.TryGetValue("clipToBounds", out var clipVal))
        {
            control.ClipToBounds = clipVal switch
            {
                bool b => b,
                string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        // === 光标：Cursor ===
        var cursorStr = props.GetValueOrDefault("cursor")?.ToString()?.ToLowerInvariant();
        if (cursorStr != null)
        {
            control.Cursor = cursorStr switch
            {
                "hand" or "pointer" => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                "text" or "ibeam" => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam),
                "wait" or "loading" => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Wait),
                "cross" or "crosshair" => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross),
                "help" => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Help),
                "no" or "forbidden" => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.No),
                _ => new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow)
            };
        }

        // === 变换：RenderTransform（rotation / scale）===
        var rotation = LayoutHelper.ParseDouble(props, "rotation");
        var scale = LayoutHelper.ParseDouble(props, "scale");
        var scaleX = LayoutHelper.ParseDouble(props, "scaleX");
        var scaleY = LayoutHelper.ParseDouble(props, "scaleY");
        if (rotation != 0 || scale != 1 || scaleX != 1 || scaleY != 1)
        {
            var transform = new TransformGroup();
            if (rotation != 0)
                transform.Children.Add(new RotateTransform(rotation));
            if (scale != 1)
                transform.Children.Add(new ScaleTransform(scale, scale));
            else if (scaleX != 1 || scaleY != 1)
                transform.Children.Add(new ScaleTransform(
                    scaleX != 1 ? scaleX : 1,
                    scaleY != 1 ? scaleY : 1));
            if (transform.Children.Count > 0)
                control.RenderTransform = transform;
        }

        // === Border 专属属性 ===
        if (control is Border border)
        {
            var cornerRadius = LayoutHelper.ParseDouble(props, "cornerRadius");
            if (cornerRadius > 0) border.CornerRadius = new CornerRadius(cornerRadius);

            var borderBrushStr = props.GetValueOrDefault("borderBrush")?.ToString()
                ?? props.GetValueOrDefault("borderColor")?.ToString();
            if (borderBrushStr != null)
                border.BorderBrush = new SolidColorBrush(Color.Parse(borderBrushStr));

            var borderThickStr = props.GetValueOrDefault("borderThickness")?.ToString();
            if (borderThickStr != null)
                border.BorderThickness = LayoutHelper.ParseThickness(borderThickStr);
        }

        // === StackPanel 专属属性 ===
        if (control is StackPanel stack)
        {
            var spacing = LayoutHelper.ParseDouble(props, "spacing");
            if (spacing > 0) stack.Spacing = spacing;
        }
    }

    /// <summary>
    /// 通用交互系统——为任意控件挂载 nav/cmd/hover/selected/disabled 交互。
    /// 关注点分离：ConvertToControl 只管"长什么样"，ApplyLayout 只管"在哪里"，ApplyInteraction 只管"怎么响应"。
    /// 任何控件只要设置了 nav/cmd/hover_source/hover_color/hover_opacity/selected_color/selected_source/disabled，
    /// 就自动获得对应的交互能力，不限于 button/imagebutton。
    /// </summary>
    private void ApplyInteraction(Control control, Dictionary<string, object> props)
    {
        // === disabled / enabled=false：禁用交互 ===
        // ApplyCommonProps（由 ApplyLayout 调用）已处理 enabled 属性设置 IsEnabled，
        // 这里检查 disabled 属性 + IsEnabled 当前状态，双重保障
        if ((props.TryGetValue("disabled", out var disVal) && disVal is bool bDis && bDis)
            || !control.IsEnabled)
        {
            control.IsEnabled = false;
            return;
        }

        var nav = props.GetValueOrDefault("nav")?.ToString();
        var cmd = props.GetValueOrDefault("cmd")?.ToString();
        var hasClick = nav != null || cmd != null;
        var hoverSource = props.GetValueOrDefault("hover_source")?.ToString();
        var hoverColorStr = props.GetValueOrDefault("hover_color") as string;
        var hoverOpacityStr = props.GetValueOrDefault("hover_opacity")?.ToString();
        var selectedColorStr = props.GetValueOrDefault("selected_color") as string;
        var selectedSource = props.GetValueOrDefault("selected_source")?.ToString();

        // isSelected 由 selected_color 和 selected_source 共享
        var isSelected = false;

        // 预计算选中态画刷（Button 专用）
        SolidColorBrush? selectedBrush = selectedColorStr != null
            ? new SolidColorBrush(Color.Parse(selectedColorStr))
            : null;

        // === Button：hover_color + selected_color 联动 ===
        if (control is Button btn)
        {
            var idleBrush = btn.Background as SolidColorBrush;
            var idleColor = idleBrush?.Color ?? Color.FromArgb(100, 80, 80, 80);
            var idleBrushCopy = new SolidColorBrush(idleColor);

            // hover 画刷：显式 hover_color > 自动加亮 > 无 hover
            SolidColorBrush hoverBrush;
            if (hoverColorStr != null)
                hoverBrush = new SolidColorBrush(Color.Parse(hoverColorStr));
            else
                hoverBrush = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Min(idleColor.A + 40, 255), idleColor.R, idleColor.G, idleColor.B));

            btn.PointerEntered += (_, _) => btn.Background = hoverBrush;
            btn.PointerExited += (_, _) =>
                btn.Background = isSelected ? (selectedBrush ?? idleBrushCopy) : idleBrushCopy;

            // selected_color 点击切换
            if (selectedBrush != null)
            {
                btn.Click += (_, _) =>
                {
                    isSelected = !isSelected;
                    btn.Background = isSelected ? selectedBrush : idleBrushCopy;
                };
            }
        }
        // === TextBlock：hover_color 改前景色 ===
        else if (control is TextBlock tb && hoverColorStr != null)
        {
            var hoverColor = Color.Parse(hoverColorStr);
            var idleFg = tb.Foreground as SolidColorBrush;
            var idleFgColor = idleFg?.Color ?? Colors.White;
            tb.PointerEntered += (_, _) => tb.Foreground = new SolidColorBrush(hoverColor);
            tb.PointerExited += (_, _) => tb.Foreground = new SolidColorBrush(idleFgColor);
        }
        // === Border：hover_color 改背景色 ===
        else if (control is Border bd && hoverColorStr != null)
        {
            var hoverColor = Color.Parse(hoverColorStr);
            var idleBg = bd.Background as SolidColorBrush;
            var idleBgColor = idleBg?.Color ?? Colors.Transparent;
            bd.PointerEntered += (_, _) => bd.Background = new SolidColorBrush(hoverColor);
            bd.PointerExited += (_, _) => bd.Background = new SolidColorBrush(idleBgColor);
        }

        // === hover_source：Image 控件换图 ===
        if (!string.IsNullOrEmpty(hoverSource) && control is Image img)
        {
            var originalSource = img.Source;
            control.PointerEntered += (_, _) => LoadSource(img, hoverSource);
            // hover 退出时恢复：如果 selected_source 已选中则保持选中图，否则恢复 idle 图
            control.PointerExited += (_, _) =>
            {
                if (!isSelected) img.Source = originalSource;
            };
        }

        // === hover_opacity：通用透明度变化 ===
        if (!string.IsNullOrEmpty(hoverOpacityStr)
            && double.TryParse(hoverOpacityStr, System.Globalization.CultureInfo.InvariantCulture, out var hoverOpacity))
        {
            var originalOpacity = control.Opacity;
            control.PointerEntered += (_, _) => control.Opacity = hoverOpacity;
            control.PointerExited += (_, _) => control.Opacity = originalOpacity;
        }

        // === selected_source：Image 控件点击切换图片 ===
        // 注意：imagebutton 合并到 image 后，控件类型是 Image 而非 Button，
        // 所以统一用 PointerPressed 实现点击切换
        if (!string.IsNullOrEmpty(selectedSource) && control is Image selImg)
        {
            var originalSource = selImg.Source;
            control.PointerPressed += (_, _) =>
            {
                isSelected = !isSelected;
                if (isSelected) LoadSource(selImg, selectedSource);
                else selImg.Source = originalSource;
            };
        }

        // === 点击行为（nav/cmd）===
        if (!hasClick) return;

        // 设置手型光标（如果未显式指定 cursor）
        if (props.GetValueOrDefault("cursor") == null)
            control.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);

        if (control is Button clickBtn)
        {
            if (nav != null)
                clickBtn.Click += (_, _) =>
                {
                    _state.Set(StateKeys.Dialog.Complete, false);
                    _pipeline.SendAsync(new NavigateCommand { Path = nav });
                };
            else if (cmd != null)
            {
                var cmdValue = props.GetValueOrDefault("value")?.ToString();
                clickBtn.Click += (_, _) =>
                {
                    _state.Set(StateKeys.Dialog.Complete, false);
                    if (_cmdService != null)
                        _ = _cmdService.ExecuteAsync(cmd, cmdValue);
                };
            }
        }
        else
        {
            // 非 Button 控件：用 PointerPressed 实现点击
            if (nav != null)
                control.PointerPressed += (_, _) =>
                {
                    _state.Set(StateKeys.Dialog.Complete, false);
                    _pipeline.SendAsync(new NavigateCommand { Path = nav });
                };
            else if (cmd != null)
            {
                var cmdValue = props.GetValueOrDefault("value")?.ToString();
                control.PointerPressed += (_, _) =>
                {
                    _state.Set(StateKeys.Dialog.Complete, false);
                    if (_cmdService != null)
                        _ = _cmdService.ExecuteAsync(cmd, cmdValue);
                };
            }
        }
    }

    /// <summary>安全解析 double 属性（支持 number 和 "50%" 字符串）</summary>
    private static double ParseDouble(Dictionary<string, object> props, string key)
    {
        if (!props.TryGetValue(key, out var val)) return key switch
        {
            "opacity" => 1.0,  // 默认不透明
            "scale" or "scaleX" or "scaleY" => 1.0,  // 默认不缩放
            _ => 0
        };
        return val switch
        {
            double d => d,
            int i => i,
            float f => f,
            string s => double.TryParse(s.TrimEnd('%'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.NumberFormatInfo.InvariantInfo, out var parsed) ? parsed : 0,
            _ => 0
        };
    }

    /// <summary>安全解析 int 属性</summary>
    private static int? ParseInt(Dictionary<string, object> props, string key)
    {
        if (!props.TryGetValue(key, out var val)) return null;
        return val switch
        {
            int i => i,
            double d => (int)d,
            string s => int.TryParse(s, out var parsed) ? parsed : null,
            _ => null
        };
    }

    /// <summary>解析 "10,5,0,0" 或 "10" 格式的 Thickness</summary>
    private static Thickness ParseThickness(string s)
    {
        var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4 && double.TryParse(parts[0], out var l)
            && double.TryParse(parts[1], out var t)
            && double.TryParse(parts[2], out var r)
            && double.TryParse(parts[3], out var b))
            return new Thickness(l, t, r, b);
        if (parts.Length == 1 && double.TryParse(parts[0], out var all))
            return new Thickness(all);
        return new Thickness(0);
    }

    /// <summary>统一解析 size 值——支持数字(double/int/float)和字符串("50%"/"640")</summary>
    private static double ResolveSize(Dictionary<string, object> props, string key, double parentSize)
    {
        if (!props.TryGetValue(key, out var val))
            return 0;
        if (val is double dv) return dv;
        if (val is int iv) return iv;
        if (val is float fv) return fv;
        if (val is string s)
        {
            s = s.Trim();
            if (s.EndsWith('%') && double.TryParse(s.AsSpan(0, s.Length - 1),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
                return parentSize * pct / 100.0;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return 0;
    }

    // ========== ConvertToControl ==========

    private Control? ConvertToControl(UIElementEntity element, double parentW = 1280, double parentH = 720, string layoutMode = "grid")
    {
        var props = element.Properties;
        var type = element.ElementType.ToLowerInvariant();

        switch (type)
        {
            case "text":
            case "dialog":
            case "narrator":
            case "speaker":
            {
                var rawText = props.GetValueOrDefault("text")?.ToString() ?? "";
                var translatedText = _i18n.Translate(rawText);
                var text = ReplaceExpressions(translatedText);
                var fontSizeStr = props.GetValueOrDefault("fontSize")?.ToString();
                var fontSize = fontSizeStr != null ? double.TryParse(fontSizeStr, out var fs) ? fs : 16 : 16;
                if (fontSize <= 0) fontSize = 16;

                var colorStr = props.GetValueOrDefault("color") as string;
                var foreground = colorStr != null ? Color.Parse(colorStr) : Colors.White;

                var alignText = props.GetValueOrDefault("textAlign")?.ToString() ?? "left";
                var textAlign = alignText.ToLowerInvariant() switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    _ => TextAlignment.Left
                };

                var maxWStr = props.GetValueOrDefault("maxWidth")?.ToString();
                var maxW = maxWStr != null ? double.TryParse(maxWStr, out var mw) ? mw : 0 : 0;

                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = fontSize,
                    Foreground = new SolidColorBrush(foreground),
                    TextAlignment = textAlign,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = maxW > 0 ? maxW : double.PositiveInfinity,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
                };

                if (rawText.Contains('{'))
                    _boundTextBlocks.Add((tb, rawText));
                return tb;
            }

            case "button":
            case "choice":
            {
                var rawBtnText = props.GetValueOrDefault("text")?.ToString() ?? "";
                var translatedBtn = _i18n.Translate(rawBtnText);
                var text = ReplaceExpressions(translatedBtn);

                var colorStr = props.GetValueOrDefault("color") as string;
                var bgColor = colorStr != null ? Color.Parse(colorStr) : Color.FromArgb(100, 80, 80, 80);

                var btnText = new TextBlock
                {
                    Text = text, Foreground = Brushes.White, FontSize = 14,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
                // 纯视觉创建——交互逻辑（nav/cmd/hover/selected/disabled）由 ApplyInteraction 统一处理
                var btn = new Button
                {
                    Content = btnText,
                    Background = new SolidColorBrush(bgColor),
                    BorderThickness = new Thickness(0),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };
                return btn;
            }

            case "vbar":
            {
                var val = props.GetValueOrDefault("value")?.ToString();
                var max = props.GetValueOrDefault("max")?.ToString();
                var current = double.TryParse(val, out var cv) ? cv : 0;
                var maximum = double.TryParse(max, out var mx) && mx > 0 ? mx : 100;
                var bar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = maximum,
                    Value = Math.Clamp(current, 0, maximum)
                };
                if (type == "vbar") bar.Orientation = Avalonia.Layout.Orientation.Vertical;
                return bar;
            }

            case "bar":
            {
                var val = props.GetValueOrDefault("value")?.ToString();
                var max = props.GetValueOrDefault("max")?.ToString();
                var current = double.TryParse(val, out var cv) ? cv : 0;
                var maximum = double.TryParse(max, out var mx) && mx > 0 ? mx : 100;
                return new ProgressBar
                {
                    Minimum = 0,
                    Maximum = maximum,
                    Value = Math.Clamp(current, 0, maximum),
                    Height = 20,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 80, 180, 255))
                };
            }

            case "background":
            {
                var source = props.GetValueOrDefault("source")?.ToString()
                    ?? props.GetValueOrDefault("path")?.ToString()
                    ?? props.GetValueOrDefault("src")?.ToString() ?? "";
                if (string.IsNullOrEmpty(source)) return null;
                var opacity = ParseOpacity(props);
                var img = new Image
                {
                    Opacity = opacity,
                    Stretch = Avalonia.Media.Stretch.UniformToFill,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
                };
                LoadSource(img, source);
                return img;
            }

            case "image":
            case "imagebutton":
            case "portrait":
            {
                var source = props.GetValueOrDefault("source")?.ToString()
                    ?? props.GetValueOrDefault("path")?.ToString()
                    ?? props.GetValueOrDefault("src")?.ToString() ?? "";
                if (string.IsNullOrEmpty(source)) return null;
                var opacity = ParseOpacity(props);

                // Stretch 模式：优先用 DSL stretch= 参数，否则自动检测
                // - size=(100%, 100%) → UniformToFill（背景图铺满全屏，允许裁切）
                // - 其他 → Uniform（立绘/图片保持比例不变形）
                var stretch = Avalonia.Media.Stretch.Uniform;
                var stretchStr = props.GetValueOrDefault("stretch")?.ToString()?.ToLowerInvariant();
                if (stretchStr != null)
                {
                    stretch = stretchStr switch
                    {
                        "fill" => Avalonia.Media.Stretch.Fill,
                        "uniformtofill" or "tofill" => Avalonia.Media.Stretch.UniformToFill,
                        _ => Avalonia.Media.Stretch.Uniform
                    };
                }
                else
                {
                    // 自动检测：宽高都是 100% → 背景图模式
                    var wIsFullPct = props.TryGetValue("width", out var wVal) && wVal?.ToString() == "100%";
                    var hIsFullPct = props.TryGetValue("height", out var hVal) && hVal?.ToString() == "100%";
                    if (wIsFullPct && hIsFullPct)
                        stretch = Avalonia.Media.Stretch.UniformToFill;
                }

                var img = new Image
                {
                    Opacity = opacity,
                    Stretch = stretch
                };
                LoadSource(img, source);
                return img;
            }

            case "panel" or "frame" or "window" or "dialogbox" or "choicebox" or "infobox" or "overlay" or "popup":
            {
                var direction = props.GetValueOrDefault("direction")?.ToString()?.ToLowerInvariant() ?? "vertical";
                var panel = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                };
                if (element.Children != null)
                {
                    var stack = new StackPanel
                    {
                        Orientation = direction == "horizontal"
                            ? Avalonia.Layout.Orientation.Horizontal
                            : Avalonia.Layout.Orientation.Vertical
                    };
                    panel.Child = stack;
                    foreach (var child in element.Children)
                    {
                        var childControl = ConvertToControl(child, parentW, parentH, layoutMode);
                        if (childControl != null)
                        {
                            ApplyLayout(childControl, child.Properties, parentW, parentH, layoutMode);
                            stack.Children.Add(childControl);
                        }
                    }
                }
                return panel;
            }

            // === Grid 容器（Avalonia 原生 Grid，支持 ColumnDefinitions/RowDefinitions）===
            case "grid":
            {
                var g = new Grid();
                var cols = props.GetValueOrDefault("columns")?.ToString();
                var rows = props.GetValueOrDefault("rows")?.ToString();
                if (cols != null)
                    g.ColumnDefinitions = ColumnDefinitions.Parse(cols);
                if (rows != null)
                    g.RowDefinitions = RowDefinitions.Parse(rows);
                if (element.Children != null)
                {
                    foreach (var child in element.Children)
                    {
                        var childControl = ConvertToControl(child, parentW, parentH, layoutMode);
                        if (childControl != null)
                        {
                            ApplyLayout(childControl, child.Properties, parentW, parentH, layoutMode);
                            g.Children.Add(childControl);
                        }
                    }
                }
                return g;
            }

            // === 独立 StackPanel（不包裹在 Border 中）===
            case "stack" or "stackpanel":
            {
                var direction = props.GetValueOrDefault("direction")?.ToString()?.ToLowerInvariant() ?? "vertical";
                var stack = new StackPanel
                {
                    Orientation = direction == "horizontal"
                        ? Avalonia.Layout.Orientation.Horizontal
                        : Avalonia.Layout.Orientation.Vertical
                };
                if (element.Children != null)
                {
                    foreach (var child in element.Children)
                    {
                        var childControl = ConvertToControl(child, parentW, parentH, layoutMode);
                        if (childControl != null)
                        {
                            ApplyLayout(childControl, child.Properties, parentW, parentH, layoutMode);
                            stack.Children.Add(childControl);
                        }
                    }
                }
                return stack;
            }

            // === ScrollViewer ===
            case "scroll" or "scrollviewer":
            {
                var scroll = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                };
                if (element.Children != null && element.Children.Count > 0)
                {
                    var content = ConvertToControl(element.Children[0], parentW, parentH, layoutMode);
                    if (content != null)
                    {
                        ApplyLayout(content, element.Children[0].Properties, parentW, parentH, layoutMode);
                        scroll.Content = content;
                    }
                }
                return scroll;
            }

            // === Viewport（可滚动区域，Phase 24，对标 Ren'Py viewport）===
            case "viewport":
            {
                var scroll = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = props.TryGetValue("scroll_h", out var sh) && sh?.ToString() == "true"
                        ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                        : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = props.TryGetValue("scroll_v", out var sv) && sv?.ToString() == "false"
                        ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                        : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                };
                if (element.Children != null && element.Children.Count > 0)
                {
                    var content = ConvertToControl(element.Children[0], parentW, parentH, layoutMode);
                    if (content != null)
                    {
                        ApplyLayout(content, element.Children[0].Properties, parentW, parentH, layoutMode);
                        scroll.Content = content;
                    }
                }
                return scroll;
            }

            // === Slider ===
            case "slider":
            {
                var min = ParseDouble(props, "min");
                var max = ParseDouble(props, "max");
                var val = ParseDouble(props, "value");
                var slider = new Slider
                {
                    Minimum = min,
                    Maximum = max > min ? max : 100,
                    Value = Math.Clamp(val, min, max > min ? max : 100)
                };
                if (props.TryGetValue("orientation", out var orient))
                    slider.Orientation = orient?.ToString()?.Equals("vertical", StringComparison.OrdinalIgnoreCase) == true
                        ? Avalonia.Layout.Orientation.Vertical
                        : Avalonia.Layout.Orientation.Horizontal;
                return slider;
            }

            // === CheckBox ===
            case "checkbox" or "check":
            {
                var rawText = props.GetValueOrDefault("text")?.ToString() ?? "";
                var translatedText = _i18n.Translate(rawText);
                var text = ReplaceExpressions(translatedText);
                var cb = new CheckBox { Content = text };
                if (props.TryGetValue("checked", out var chkVal))
                {
                    cb.IsChecked = chkVal switch
                    {
                        bool b => b,
                        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
                        _ => false
                    };
                }
                return cb;
            }

            // === Canvas（绝对定位容器）===
            case "canvas":
            {
                var canvas = new Canvas();
                if (element.Children != null)
                {
                    foreach (var child in element.Children)
                    {
                        var childControl = ConvertToControl(child, parentW, parentH, layoutMode);
                        if (childControl != null)
                        {
                            ApplyLayout(childControl, child.Properties, parentW, parentH, "canvas");
                            // Canvas 子元素用 Canvas.Left/Top 定位
                            var cx = ResolveSize(child.Properties, "x", parentW);
                            var cy = ResolveSize(child.Properties, "y", parentH);
                            if (cx > 0) Canvas.SetLeft(childControl, cx);
                            if (cy > 0) Canvas.SetTop(childControl, cy);
                            canvas.Children.Add(childControl);
                        }
                    }
                }
                return canvas;
            }

            // === 独立 Border（仅边框/背景，不自动包含 StackPanel）===
            case "border":
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                };
                if (element.Children != null && element.Children.Count > 0)
                {
                    var childControl = ConvertToControl(element.Children[0], parentW, parentH, layoutMode);
                    if (childControl != null)
                    {
                        ApplyLayout(childControl, element.Children[0].Properties, parentW, parentH, layoutMode);
                        border.Child = childControl;
                    }
                }
                return border;
            }

            // === Separator ===
            case "separator":
            {
                return new Separator
                {
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
                };
            }

            // === Spacer（空白占位）===
            case "spacer":
            {
                var w = ResolveSize(props, "width", parentW);
                var h = ResolveSize(props, "height", parentH);
                return new Control
                {
                    Width = w > 0 ? w : 10,
                    Height = h > 0 ? h : 10
                };
            }

            case "video":
            {
                var source = props.GetValueOrDefault("source")?.ToString()
                    ?? props.GetValueOrDefault("path")?.ToString()
                    ?? props.GetValueOrDefault("src")?.ToString() ?? "";
                if (string.IsNullOrEmpty(source)) return null;

                var opacity = ParseOpacity(props);
                var videoControl = new MediaPlayer.Controls.GpuMediaPlayer
                {
                    Opacity = opacity,
                    AutoPlay = true,
                    Volume = 0, // 永久静音——音视频分离架构，音频走 AudioManager
                };

                // 尝试设置 Source（支持相对路径 → 绝对 URI）
                try
                {
                    videoControl.Source = new Uri(System.IO.Path.GetFullPath(source));
                }
                catch
                {
                    // 路径无法解析时忽略——UpdateVideoPlayer 会根据状态键重新设置
                }

                return videoControl;
            }

            default:
                return null;
        }
    }

    // ========== DialogBox 布局 ==========

    private void UpdateDialogLayout()
    {
        // Phase 27：对话框在设计分辨率内布局，不随窗口缩放重算
        ApplyDialogLayout(_designWidth, _designHeight);
    }

    /// <summary>
    /// Phase 24: window 窗口模式控制对话框可见性
    /// <para>auto: 有对话文本时显示，无文本时隐藏</para>
    /// <para>show: 强制显示</para>
    /// <para>hide: 强制隐藏</para>
    /// </summary>
    private void UpdateWindowMode(string dialogText)
    {
        if (_dialogBoxIF == null) return;
        var mode = _state.Get<string>(StateKeys.Dialog.WindowMode) ?? "auto";
        var dlgCtrl = _dialogBoxIF.AsControl();
        switch (mode)
        {
            case "show":
                dlgCtrl.IsVisible = true;
                break;
            case "hide":
                dlgCtrl.IsVisible = false;
                break;
            default: // auto
                dlgCtrl.IsVisible = !string.IsNullOrEmpty(dialogText);
                break;
        }
    }


    private double _lastW, _lastH, _lastML, _lastMB;
    private void ApplyDialogLayout(double parentW, double parentH)
    {
        var w = _state.Get<double?>(StateKeys.Dialog.WidthPercent) ?? _state.Get<double?>(StateKeys.Dialog.WidthPercentDefault);
        var h = _state.Get<double?>(StateKeys.Dialog.HeightPercent) ?? _state.Get<double?>(StateKeys.Dialog.HeightPercentDefault);
        var ml = _state.Get<double?>(StateKeys.Dialog.MarginLeft) ?? _state.Get<double?>(StateKeys.Dialog.MarginLeftDefault);
        var mb = _state.Get<double?>(StateKeys.Dialog.MarginBottom) ?? _state.Get<double?>(StateKeys.Dialog.MarginBottomDefault);

if (_dialogBoxIF == null) return;

var dlgCtrl = _dialogBoxIF.AsControl();
dlgCtrl.Width = w.HasValue ? parentW * w.Value / 100.0 : double.NaN;
dlgCtrl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
dlgCtrl.MaxHeight = h.HasValue ? parentH * h.Value / 100.0 : parentH * 0.35;
dlgCtrl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
dlgCtrl.Margin = new Avalonia.Thickness(ml ?? 0, 0, 0, mb ?? 0);
        _lastW = w ?? -1; _lastH = h ?? -1; _lastML = ml ?? -1; _lastMB = mb ?? -1;
    }

    // ========== 菜单/输入/运行时 ==========

    private void UpdateMenuOverlay()
    {
        // 同时适配 C# (__menu_options) 和 DSL (__dsl_menu_options) 两套键名
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
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            var stack = new StackPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
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

    private void UpdateInputOverlay()
    {
        // 同时适配 C# (__input_prompt) 和 DSL (__dsl_input_prompt) 两套键名
        var prompt = _state.Get<string>(StateKeys.Input.Prompt) ?? _state.Get<string>(StateKeys.Input.DslPrompt);
        if (!string.IsNullOrEmpty(prompt))
        {
            if (_inputPanel != null) return;
            var root = _sceneRoot;
            if (root == null) return;
            _inputPanel = new Panel
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };
            var stack = new StackPanel { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = prompt, Foreground = Brushes.White, FontSize = 20, Margin = new Thickness(0, 0, 0, 15) });
            _inputBox = new TextBox { Width = 400, Height = 40, FontSize = 18, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(100, 50, 50, 50)) };
            stack.Children.Add(_inputBox);
            var submitBtn = new Button { Content = "确定", Width = 120, Height = 40, Margin = new Thickness(0, 10), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
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
            var ctrl = ConvertToControl(el, parentW, parentH, _currentLayoutMode);
            if (ctrl == null) continue;
            ctrl.Tag = el.Properties.TryGetValue(StateKeys.UiTags.Tag, out var tagVal) ? tagVal?.ToString() : StateKeys.UiTags.Runtime;
            ctrl.SetValue(ZIndexProperty, 50); // 立绘在对话框下方
            ApplyLayout(ctrl, el.Properties, parentW, parentH, _currentLayoutMode);
            ApplyInteraction(ctrl, el.Properties);
            root.Children.Add(ctrl);
        }
    }

    // ========== Notify ==========

    private void UpdateNotifyToast()
    {
        var text = _state.Get<string>(StateKeys.Notify.Text);

        // 检查是否有新通知
        if (text != null)
        {
            var type = _state.Get<string>(StateKeys.Notify.Type) ?? "info";
            var root = _outerGrid;
            if (root == null) return;

            // 移除旧通知
            RemoveNotifyToast(root);

            // 根据类型选择颜色和图标
            var (icon, bg, fg) = type switch
            {
                "warning" => ("⚠", 
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(200, 180, 120, 0)),
                    Avalonia.Media.Brushes.White),
                "error" => ("✖", 
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(200, 160, 30, 30)),
                    Avalonia.Media.Brushes.White),
                _ => ("ℹ", 
                    new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(200, 30, 60, 100)),
                    Avalonia.Media.Brushes.White),
            };

            var notify = new Avalonia.Controls.Border
            {
                Background = bg,
                CornerRadius = new Avalonia.CornerRadius(8),
                Padding = new Avalonia.Thickness(20, 10),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Avalonia.Thickness(0, 80, 0, 0),
                Tag = StateKeys.UiTags.Notify,
                Opacity = 0,
                Child = new Avalonia.Controls.TextBlock
                {
                    Text = $"{icon}  {text}",
                    Foreground = fg,
                    FontSize = 16,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                }
            };

            root.Children.Add(notify);
            _currentNotify = notify;
            _state.Set(StateKeys.Notify.Text, (string?)null);
            _state.Set(StateKeys.Notify.Type, (string?)null);
            _notifyRemainSeconds = 3.0;
            _notifyFadeSeconds = NotifyFadeDuration; // 淡入
        }
        else if (_notifyRemainSeconds > 0)
        {
            _notifyRemainSeconds -= 0.016;

            // 淡入/淡出动画
            if (_notifyFadeSeconds > 0)
            {
                _notifyFadeSeconds -= 0.016;
                if (_currentNotify != null)
                {
                    // 淡入：opacity 从 0 → 1
                    var progress = 1.0 - (_notifyFadeSeconds / NotifyFadeDuration);
                    _currentNotify.Opacity = Math.Clamp(progress, 0, 1);
                }
            }

            if (_notifyRemainSeconds <= 0)
            {
                // 开始淡出
                _notifyFadeSeconds = -NotifyFadeDuration;
                _notifyRemainSeconds = 0;
            }
        }
        else if (_notifyFadeSeconds < 0)
        {
            // 淡出阶段
            _notifyFadeSeconds += 0.016;
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

    /// <summary>移除当前通知 Toast</summary>
    private void RemoveNotifyToast(Avalonia.Controls.Panel root)
    {
        for (int i = root.Children.Count - 1; i >= 0; i--)
            if (root.Children[i].Tag?.ToString() == StateKeys.UiTags.Notify)
                root.Children.RemoveAt(i);
        _currentNotify = null;
    }

    // ========== 动画 ==========

    /// <summary>
    /// 每帧读取 __anim_*_current 状态，更新运行时控件的 Margin/Opacity
    /// </summary>
    private void ApplyAnimations()
    {
        var root = _sceneRoot;
        if (root == null) return;

        // 扫描所有 __anim_*_active 标记
        foreach (var key in _state.Keys)
        {
            if (key is not string sk || !sk.EndsWith("_active")) continue;
            if (!_state.Get<bool>(sk)) continue;

            var baseKey = sk[..^7];
            var parts = baseKey.Split('_');
            if (parts.Length < 4) continue;

            var target = string.Join("_", parts.Skip(3).Take(parts.Length - 4));
            var property = parts[^1];

            var current = _state.Get<double>(baseKey + "_current");
            if (double.IsNaN(current)) continue;

            Control? match = null;
            foreach (var child in root.Children)
            {
                if (child is Control c && c.Tag?.ToString() == target)
                {
                    match = c;
                    break;
                }
            }
            if (match == null) continue;

            switch (property)
            {
                case "x":
                    var curMargin = match.Margin;
                    match.Margin = new Thickness(current, curMargin.Top, curMargin.Right, curMargin.Bottom);
                    break;
                case "y":
                    curMargin = match.Margin;
                    match.Margin = new Thickness(curMargin.Left, current, curMargin.Right, curMargin.Bottom);
                    break;
                case "opacity":
                    match.Opacity = Math.Clamp(current, 0, 1);
                    break;
                case "scale":
                case "zoom":
                    match.RenderTransform = new ScaleTransform(current, current);
                    break;
                case "rotate":
                    match.RenderTransform = new RotateTransform(current);
                    break;
            }
        }
    }

    // ========== 对话/表达式 ==========

    private void UpdateDialog(string text)
    {
        if (string.IsNullOrEmpty(text)) _dialogBoxIF?.Hide();
        else
        {
            var speaker = _state.Get<string>(StateKeys.Dialog.Speaker);
            _dialogBoxIF?.SetText(text, speaker);
        }
    }

    /// <summary>
    /// 性能 HUD——显示 FPS、帧时间、命令队列、DSL 进度、动画数、内存等
    /// </summary>
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
                Padding = new Avalonia.Thickness(6, 4),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
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

    /// <summary>
    /// 从视觉树移除并清理视频播放器
    /// </summary>
    private void RemoveVideoPlayerFromTree()
    {
        if (_videoPlayer == null) return;
        _videoPlayer.Stop();
        _sceneRoot?.Children.Remove(_videoPlayer);
        _videoPlayer = null;
        _lastVideoPath = "";
        _lastVideoIsPlaying = false;
        _lastVideoPosition = -1;
        _lastVideoDuration = -1;
        _lastVideoFinished = false;
    }

    /// <summary>
    /// 视频播放器同步——每帧读取 __video_* 状态键，驱动 GpuMediaPlayer 控件
    /// <para>状态驱动模式：VideoManager 写状态键 → SceneView 读状态键渲染</para>
    /// <para>音视频分离架构：GpuMediaPlayer 永久静音（Volume=0），视频纯视觉，音频走 AudioManager</para>
    /// </summary>
    private void UpdateVideoPlayer()
    {
        var videoPath = _state.Get<string>(StateKeys.Video.CurrentPath) ?? "";
        var cutsceneActive = _state.Get<bool>(StateKeys.Video.CutsceneActive);

        // 路径变化 → 重建播放器
        if (videoPath != _lastVideoPath)
        {
            // 清理旧播放器（从视觉树移除）
            RemoveVideoPlayerFromTree();
            _lastVideoPath = videoPath;

            if (!string.IsNullOrEmpty(videoPath))
            {
                // 创建新播放器
                _videoPlayer = new MediaPlayer.Controls.GpuMediaPlayer
                {
                    AutoPlay = _state.Get<bool>(StateKeys.Video.AutoPlay),
                    Volume = 0, // 永久静音——音视频分离架构，音频走 AudioManager
                    ZIndex = cutsceneActive ? 100 : 0,
                };

                try
                {
                    _videoPlayer.Source = new Uri(System.IO.Path.GetFullPath(videoPath));
                }
                catch
                {
                    // 路径无效——忽略
                }

                // 添加到场景根容器（在缩放层内，随场景缩放）
                _sceneRoot?.Children.Add(_videoPlayer);
            }
            return;
        }

        if (_videoPlayer == null || string.IsNullOrEmpty(videoPath))
        {
            var skipable = _state.Get<bool>(StateKeys.Video.CutsceneSkipable);
            UpdateCutsceneMask(cutsceneActive, skipable);
            return;
        }

        // 同步 ZIndex（过场模式覆盖一切）
        _videoPlayer.ZIndex = cutsceneActive ? 100 : 0;

        // 检测 IsFinished 被外部重置（如 VideoManager.Play 重新播放同一视频）
        var currentIsFinished = _state.Get<bool>(StateKeys.Video.IsFinished);
        if (!currentIsFinished && _lastVideoFinished)
        {
            _lastVideoFinished = false;
        }

        // 同步播放/暂停状态（仅在状态变化时调用，避免每帧重复调用）
        var isPlaying = _state.Get<bool>(StateKeys.Video.IsPlaying);
        var isPaused = _state.Get<bool>(StateKeys.Video.IsPaused);
        var shouldPlay = isPlaying && !isPaused;

        if (shouldPlay != _lastVideoIsPlaying)
        {
            if (shouldPlay)
                _videoPlayer.Play();
            else if (isPaused)
                _videoPlayer.Pause();
            _lastVideoIsPlaying = shouldPlay;
        }

        // 处理跳转
        var seekPos = _state.Get<double?>(StateKeys.Video.SeekPosition);
        if (seekPos.HasValue)
        {
            _videoPlayer.Seek(TimeSpan.FromSeconds(seekPos.Value));
            _state.Set<object?>(StateKeys.Video.SeekPosition, null);
        }

        // 回写位置和时长（仅在变化时写入，避免每帧高频状态写入）
        var currentPos = _videoPlayer.Position.TotalSeconds;
        var currentDur = _videoPlayer.Duration.TotalSeconds;
        if (Math.Abs(currentPos - _lastVideoPosition) > 0.05)
        {
            _state.Set(StateKeys.Video.Position, currentPos);
            _lastVideoPosition = currentPos;
        }
        if (Math.Abs(currentDur - _lastVideoDuration) > 0.05)
        {
            _state.Set(StateKeys.Video.Duration, currentDur);
            _lastVideoDuration = currentDur;
        }

        // 播放结束检测（Position 接近 Duration 且 Duration > 0）
        if (currentDur > 0 && currentPos >= currentDur - 0.15 && shouldPlay && !_lastVideoFinished)
        {
            var loop = _state.Get<bool>(StateKeys.Video.Loop);
            if (loop)
            {
                // 循环模式：从头重播
                _videoPlayer.Seek(TimeSpan.Zero);
            }
            else
            {
                // 非循环模式：标记结束
                _state.Set(StateKeys.Video.IsPlaying, false);
                _state.Set(StateKeys.Video.IsFinished, true);
                _lastVideoIsPlaying = false;
                _lastVideoFinished = true;

                // 如果在过场模式，清除过场标记
                if (cutsceneActive)
                {
                    _state.Set(StateKeys.Video.CutsceneActive, false);
                }
            }
        }

        // 过场遮罩更新
        var cutsceneSkipable = _state.Get<bool>(StateKeys.Video.CutsceneSkipable);
        UpdateCutsceneMask(cutsceneActive, cutsceneSkipable);
    }

    /// <summary>
    /// 更新过场动画遮罩
    /// <para>过场模式 + skipable=true 时显示透明全屏遮罩（ZIndex=101），拦截点击用于跳过</para>
    /// <para>skipable=false 时不显示遮罩，用户无法跳过，必须等待视频自然结束</para>
    /// </summary>
    private void UpdateCutsceneMask(bool cutsceneActive, bool skipable)
    {
        if (!cutsceneActive || !skipable)
        {
            if (_cutsceneMask != null)
            {
                _cutsceneMask.IsVisible = false;
            }
            return;
        }

        if (_cutsceneMask == null)
        {
            _cutsceneMask = new Border
            {
                Background = Brushes.Transparent,
                ZIndex = 101,
                IsHitTestVisible = true,
            };
            _cutsceneMask.PointerPressed += (_, _) =>
            {
                // 用户点击 → 标记跳过
                _state.Set(StateKeys.Video.CutsceneSkipped, true);
                _state.Set(StateKeys.Video.CutsceneActive, false);
                _state.Set(StateKeys.Video.IsPlaying, false);
                _state.Set(StateKeys.Video.IsFinished, true);
            };

            // 过场遮罩加入 _outerGrid（在缩放层外，全屏不缩放）
            _outerGrid?.Children.Add(_cutsceneMask);
        }

        _cutsceneMask.IsVisible = true;
    }

    /// <summary>
    /// 更新对话模态遮罩可见性
    /// <para>仅在 DSL 等待对话（waiting_type=dialog）且 Clickable=false 时显示</para>
    /// <para>遮罩透明覆盖场景元素，拦截点击推进对话而非触发按钮</para>
    /// </summary>
private void UpdateDialogMask()
{
    if (_dialogMask == null) return;
    var waitingType = _state.Get<string>(StateKeys.Dsl.WaitingType) ?? "";
    var isClickable = _state.Get<bool>(StateKeys.Dialog.Clickable);
    // 遮罩在 dialog / wait_skipable / pause 期间显示（拦截点击推进/跳过）
    _dialogMask.IsVisible = (waitingType == StateKeys.Dsl.WaitingTypes.Dialog
        || waitingType == StateKeys.Dsl.WaitingTypes.WaitSkipable
        || waitingType == StateKeys.Dsl.WaitingTypes.Pause)
        && !isClickable;
}

    private string ReplaceExpressions(string text) => ExpressionParser.Replace(text, _state);

    private bool CheckVarChanges()
    {
        _varCheckCounter++;
        if (_varCheckCounter % 5 != 0) return false; // 每 5 帧检测一次
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

    private void RefreshBoundTextBlocks()
    {
        foreach (var (tb, raw) in _boundTextBlocks)
        {
            var replaced = ReplaceExpressions(_i18n.Translate(raw));
            if (tb.Text != replaced) tb.Text = replaced;
        }
    }

    /// <summary>LRU 图片缓存——限制上限并自动 Dispose 旧 Bitmap，防止长时间运行 OOM</summary>
    private static LruImageCache s_imageCache = new(128);

    /// <summary>设置图片缓存上限（由 SceneView 构造函数从 Options 传入）</summary>
    internal static void ConfigureImageCache(int maxCapacity)
    {
        if (maxCapacity <= 0) return;
        if (s_imageCache.Count == 0 || maxCapacity == s_imageCache.Count)
        {
            // 空缓存时直接替换
            s_imageCache.Dispose();
            s_imageCache = new LruImageCache(maxCapacity);
        }
    }

    private static double ParseOpacity(Dictionary<string, object> props)
    {
        var s = props.GetValueOrDefault("opacity")?.ToString();
        return s != null && double.TryParse(s, out var op) && op > 0 ? op : 1.0;
    }

    private static void LoadSource(Image img, string source)
    {
        var cached = s_imageCache.TryGet(source);
        if (cached != null)
        {
            img.Source = cached;
            return;
        }
        if (source.StartsWith("avares://"))
        {
            try
            {
                var bmp = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri(source)));
                s_imageCache.Add(source, bmp);
                img.Source = bmp;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SceneView] LoadSource(avares) failed: {source} — {ex.Message}"); }
        }
        else _ = LoadImageAsync(img, source);
    }

    /// <summary>异步加载图片到 Image 控件（fire-and-forget，异常安全）</summary>
    private static async Task LoadImageAsync(Image img, string path)
    {
        try
        {
            var fullPath = path;
            if (!System.IO.Path.IsPathRooted(path))
            {
                var candidate = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), path);
                if (System.IO.File.Exists(candidate)) fullPath = candidate;
            }
            if (!System.IO.File.Exists(fullPath)) return;
            // 检查缓存（可能在异步等待期间被其他线程写入）
            var cached = s_imageCache.TryGet(fullPath);
            if (cached != null)
            {
                img.Source = cached;
                return;
            }
            var bitmap = await Task.Run(() => new Avalonia.Media.Imaging.Bitmap(fullPath));
            s_imageCache.Add(fullPath, bitmap);
            img.Source = bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SceneView] LoadImage failed: {path} — {ex.Message}");
        }
    }
}
