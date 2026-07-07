using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Views;

public class SceneView : UserControl
{
    private readonly IStateContainer _state;
    private readonly ICommandPipeline _pipeline;
    private readonly II18nService _i18n;
    private readonly ICommandService? _cmdService;
    private readonly ISceneRegistry? _sceneRegistry;
    private string _lastSceneName = "";
    private string _lastDialogText = "";
    private string _lastLanguage = "";
    private readonly List<(TextBlock tb, string rawText)> _boundTextBlocks = new();
    private Views.DialogBox? _dialogBox;
    private Border? _transitionOverlay;
    private Panel? _menuPanel;
    private Panel? _inputPanel;
    private TextBox? _inputBox;

    // ── 布局追踪：窗口缩放时重算百分比定位/尺寸 ──
    private bool _layoutDirty;
    private string _currentLayoutMode = "grid";
    private readonly List<(Control control, Dictionary<string, object> props)> _trackedControls = new();

    private int _varCheckCounter;
    private double _notifyRemainSeconds;
    private readonly Dictionary<string, object?> _lastVarValues = new();

    public SceneView(IStateContainer state, ICommandPipeline pipeline,
        II18nService i18n,
        ICommandService? cmdService = null,
        ISceneRegistry? sceneRegistry = null)
    {
        _state = state;
        _pipeline = pipeline;
        _i18n = i18n;
        _cmdService = cmdService;
        _sceneRegistry = sceneRegistry;
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
                if (_dialogBox != null && !_dialogBox.IsComplete)
                    _dialogBox.SkipToEnd();
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
    catch { return null; }
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
            if (Content is Panel rootPanel && (offsetX != 0 || offsetY != 0 || scale != 1.0))
            {
                var transform = new TransformGroup();
                if (offsetX != 0 || offsetY != 0)
                    transform.Children.Add(new TranslateTransform(offsetX, offsetY));
                if (scale != 1.0 && scale > 0)
                    transform.Children.Add(new ScaleTransform(scale, scale));
                rootPanel.RenderTransform = transform;
            }
            else if (Content is Panel rp && transActive)
            {
                rp.RenderTransform = null;
            }
        }

        // 屏幕震动效果——在过渡之上叠加震动偏移
        var shakeActive = _state.Get<bool>(StateKeys.Shake.Active);
        var shakeOffsetX = _state.Get<double>(StateKeys.Shake.OffsetX);
        var shakeOffsetY = _state.Get<double>(StateKeys.Shake.OffsetY);
        if (Content is Panel shakePanel)
        {
            if (shakeActive && (shakeOffsetX != 0 || shakeOffsetY != 0))
            {
                // 叠加震动偏移到现有变换之上
                var existingTransform = shakePanel.RenderTransform as TransformGroup;
                var shakeTransform = new TransformGroup();
                // 保留过渡变换
                if (existingTransform != null)
                {
                    foreach (var child in existingTransform.Children)
                        shakeTransform.Children.Add(child);
                }
                shakeTransform.Children.Add(new TranslateTransform(shakeOffsetX, shakeOffsetY));
                shakePanel.RenderTransform = shakeTransform;
            }
            else if (!shakeActive && !transActive)
            {
                // 无震动无过渡时清除变换
                shakePanel.RenderTransform = null;
            }
        }

        var curLang = _state.Get<string>(StateKeys.Scene.CurrentLanguage) ?? "";
        if (curLang != _lastLanguage)
        {
            if (!string.IsNullOrEmpty(curLang)) _i18n.SwitchLanguage(curLang);
            _lastLanguage = curLang;
        }

        // 场景切换 / 脏标记才重建；窗口 resize 由 Avalonia 原生布局处理，不重建
        if (sceneName != _lastSceneName)
        {
            RebuildScene(sceneName);
            _lastSceneName = sceneName;
        }
        else if (_state.Get<bool>(StateKeys.Scene.Dirty))
        {
            _state.Set(StateKeys.Scene.Dirty, false);
            RebuildScene(sceneName);
            _lastSceneName = sceneName;
        }
        else if (sceneName == _lastSceneName && !string.IsNullOrEmpty(sceneName))
        {
            if (CheckVarChanges()) RefreshBoundTextBlocks();
        }

        // 窗口缩放重布局：百分比定位/尺寸的控件需要重新计算
        if (_layoutDirty)
        {
            _layoutDirty = false;
            RelayoutAllControls();
        }

        RefreshBoundTextBlocks();

        if (dialogText != _lastDialogText)
        {
            UpdateDialog(dialogText);
            _lastDialogText = dialogText;
        }
        _dialogBox?.Advance(delta);

        UpdateDialogLayout();
        UpdateMenuOverlay();
        UpdateInputOverlay();
        UpdateRuntimeElements();
        UpdateNotifyToast();
        ApplyAnimations();
    }

    private void RebuildScene(string sceneName)
    {
        _boundTextBlocks.Clear();
        _trackedControls.Clear();
        _lastDialogText = ""; // 清除旧对话防止场景切换后残留在新 DialogBox
        _dialogBox?.Hide();
        var elements = _state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        if (elements == null || elements.Count == 0)
        {
            Content = new TextBlock { Text = $"场景 [{sceneName}]", Foreground = Brushes.Gray };
            return;
        }

        // 读取场景布局模式（从 SceneEntity 获取，默认 grid）
        var sceneEntity = _sceneRegistry?.FindScene(sceneName);
        _currentLayoutMode = sceneEntity?.LayoutMode ?? "grid";

        var parentW = Bounds.Width > 0 ? Bounds.Width : 1280;
        var parentH = Bounds.Height > 0 ? Bounds.Height : 720;

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

        rootPanel.PointerPressed += (_, _) =>
        {
            // 任意点击推进对话/结束硬暂停（对标 Ren'Py click to advance）
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
            // 设置 Tag 用于动画匹配（AnimateCommand 通过 Tag 找到目标控件）
            if (element.Properties.TryGetValue(StateKeys.UiTags.Tag, out var tag) && tag is string s)
                control.Tag = s;
            // 追踪控件用于窗口缩放重布局
            _trackedControls.Add((control, element.Properties));
            rootPanel.Children.Add(control);
        }

        _dialogBox = new Views.DialogBox(_state);
        _dialogBox.SetValue(Grid.ZIndexProperty, 100);
        _dialogBox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        _dialogBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        ApplyDialogLayout(parentW, parentH);
        if (!string.IsNullOrEmpty(_lastDialogText))
            _dialogBox.SetText(_lastDialogText, _state.Get<string>(StateKeys.Dialog.Speaker));

        _transitionOverlay = new Border
        {
            Background = Brushes.Black,
            IsVisible = false,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };

        if (_dialogBox != null) rootPanel.Children.Add(_dialogBox);
        rootPanel.Children.Add(_transitionOverlay);
        Content = rootPanel;
    }

    /// <summary>
    /// 窗口缩放时重新计算所有追踪控件的百分比定位/尺寸
    /// </summary>
    private void RelayoutAllControls()
    {
        if (_trackedControls.Count == 0) return;
        var parentW = Bounds.Width > 0 ? Bounds.Width : 1280;
        var parentH = Bounds.Height > 0 ? Bounds.Height : 720;
        foreach (var (control, props) in _trackedControls)
        {
            ApplyLayout(control, props, parentW, parentH, _currentLayoutMode);
        }
        ApplyDialogLayout(parentW, parentH);
    }

    // ========== 统一布局 ==========

    /// <summary>
    /// 统一映射到 Avalonia 原生布局属性
    /// <para>百分比精确定位：x=25% → Margin.Left = parentW * 0.25（窗口缩放时重算）</para>
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
            bool hasX = false, hasY = false;

            if (xVal != null)
            {
                marginLeft = LayoutHelper.ResolvePercentPosition(xVal, pw) + xoffset;
                hasX = true;
                // 锚点偏移
                var xanchor = LayoutHelper.ParseDouble(props, "xanchor");
                if (xanchor > 0 && !double.IsNaN(control.Width))
                    marginLeft -= control.Width * xanchor;
            }
            if (yVal != null)
            {
                marginTop = LayoutHelper.ResolvePercentPosition(yVal, ph) + yoffset;
                hasY = true;
                var yanchor = LayoutHelper.ParseDouble(props, "yanchor");
                if (yanchor > 0 && !double.IsNaN(control.Height))
                    marginTop -= control.Height * yanchor;
            }

            var rightVal = props.GetValueOrDefault("right");
            var bottomVal = props.GetValueOrDefault("bottom");
            double marginRight = 0, marginBottom = 0;
            bool hasRight = false, hasBottom = false;

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
                var nav = props.GetValueOrDefault("nav")?.ToString();
                var cmd = props.GetValueOrDefault("cmd")?.ToString();

                var colorStr = props.GetValueOrDefault("color") as string;
                var bgColor = colorStr != null ? Color.Parse(colorStr) : Color.FromArgb(100, 80, 80, 80);
                var hoverColorStr = props.GetValueOrDefault("hover_color") as string;
                var hoverColor = hoverColorStr != null ? Color.Parse(hoverColorStr) : default(Color?);

                var selectedColorStr = props.GetValueOrDefault("selected_color") as string;
                var selectedBrush = selectedColorStr != null ? new SolidColorBrush(Color.Parse(selectedColorStr)) : null;

                var btnText = new TextBlock
                {
                    Text = text, Foreground = Brushes.White, FontSize = 14,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };
                var idleBrush = new SolidColorBrush(bgColor);
                var hoverBrush = hoverColor.HasValue
                    ? new SolidColorBrush(hoverColor.Value)
                    : new SolidColorBrush(Color.FromArgb((byte)Math.Min(bgColor.A + 40, 255), bgColor.R, bgColor.G, bgColor.B));
                var btn = new Button
                {
                    Content = btnText,
                    Background = idleBrush,
                    BorderThickness = new Thickness(0),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };
                btn.PointerEntered += (_, _) => btn.Background = hoverBrush;
                var isSelected = false;
                btn.PointerExited += (_, _) => btn.Background = isSelected ? (selectedBrush ?? idleBrush) : idleBrush;

                if (nav != null)
                    btn.Click += (_, _) =>
                    {
                        _state.Set(StateKeys.Dialog.Complete, false);
                        _pipeline.SendAsync(new NavigateCommand { Path = nav });
                    };
                else if (cmd != null)
                {
                    var cmdValue = props.GetValueOrDefault("value")?.ToString();
                    btn.Click += (_, _) =>
                    {
                        _state.Set(StateKeys.Dialog.Complete, false);
                        if (_cmdService != null)
                            _ = _cmdService.ExecuteAsync(cmd, cmdValue);
                    };
                }

                if (props.TryGetValue("disabled", out var dis) && dis is bool bDis && bDis)
                    btn.IsEnabled = false;

                if (selectedBrush != null)
                    btn.Click += (_, _) => { isSelected = !isSelected; btn.Background = isSelected ? selectedBrush : idleBrush; };

                return btn;
            }

            case "imagebutton":
            {
                var source = props.GetValueOrDefault("source")?.ToString() ?? "";
                if (string.IsNullOrEmpty(source)) return null;
                var nav = props.GetValueOrDefault("nav")?.ToString();
                var cmd = props.GetValueOrDefault("cmd")?.ToString();
                var opacity = ParseOpacity(props);

                var img = new Image { Opacity = opacity, Stretch = Avalonia.Media.Stretch.Uniform };
                LoadSource(img, source);
                var btn = new Button
                {
                    Content = img,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                };
                if (nav != null)
                    btn.Click += (_, _) => { _state.Set(StateKeys.Dialog.Complete, false); _pipeline.SendAsync(new NavigateCommand { Path = nav }); };
                else if (cmd != null)
                {
                    var cmdValue = props.GetValueOrDefault("value")?.ToString();
                    btn.Click += (_, _) => { _state.Set(StateKeys.Dialog.Complete, false); if (_cmdService != null) _ = _cmdService.ExecuteAsync(cmd, cmdValue); };
                }
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

            default:
                return null;
        }
    }

    // ========== DialogBox 布局 ==========

    private void UpdateDialogLayout()
    {
        var w = Bounds.Width > 0 ? Bounds.Width : 1280;
        var h2 = Bounds.Height > 0 ? Bounds.Height : 720;
        ApplyDialogLayout(w, h2);
    }


    private double _lastW, _lastH, _lastML, _lastMB;
    private void ApplyDialogLayout(double parentW, double parentH)
    {
        var w = _state.Get<double?>(StateKeys.Dialog.WidthPercent) ?? _state.Get<double?>(StateKeys.Dialog.WidthPercentDefault);
        var h = _state.Get<double?>(StateKeys.Dialog.HeightPercent) ?? _state.Get<double?>(StateKeys.Dialog.HeightPercentDefault);
        var ml = _state.Get<double?>(StateKeys.Dialog.MarginLeft) ?? _state.Get<double?>(StateKeys.Dialog.MarginLeftDefault);
        var mb = _state.Get<double?>(StateKeys.Dialog.MarginBottom) ?? _state.Get<double?>(StateKeys.Dialog.MarginBottomDefault);

        if (_dialogBox == null) return;

        _dialogBox.Width = w.HasValue ? parentW * w.Value / 100.0 : double.NaN;
        _dialogBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _dialogBox.MaxHeight = h.HasValue ? parentH * h.Value / 100.0 : parentH * 0.35;
        _dialogBox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        _dialogBox.Margin = new Avalonia.Thickness(ml ?? 0, 0, 0, mb ?? 0);
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
            var root = (Content as Grid) ?? Content as Panel;
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
            var root = (Content as Grid) ?? Content as Panel;
            root?.Children.Remove(_menuPanel);
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
            var root = (Content as Grid) ?? Content as Panel;
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
            var root = (Content as Grid) ?? Content as Panel;
            root?.Children.Remove(_inputPanel);
            _inputPanel = null; _inputBox = null;
        }
    }

    private void UpdateRuntimeElements()
    {
        var rt = _state.Get<object>(StateKeys.Scene.RuntimeElements);
        if (rt is not List<UIElementEntity> elements) return;
        var root = (Content as Grid) ?? Content as Panel;
        if (root == null) return;
        for (int i = root.Children.Count - 1; i >= 0; i--)
            if (root.Children[i] is Control c && c.Tag?.ToString() == StateKeys.UiTags.Runtime)
                root.Children.RemoveAt(i);
        var parentW = Bounds.Width > 0 ? Bounds.Width : 1280;
        var parentH = Bounds.Height > 0 ? Bounds.Height : 720;
        foreach (var el in elements)
        {
            var ctrl = ConvertToControl(el, parentW, parentH, _currentLayoutMode);
            if (ctrl == null) continue;
            ctrl.Tag = el.Properties.TryGetValue(StateKeys.UiTags.Tag, out var tagVal) ? tagVal?.ToString() : StateKeys.UiTags.Runtime;
            ctrl.SetValue(ZIndexProperty, 50); // 立绘在对话框下方
            ApplyLayout(ctrl, el.Properties, parentW, parentH, _currentLayoutMode);
            root.Children.Add(ctrl);
        }
    }

    // ========== Notify ==========

    private void UpdateNotifyToast()
    {
        var text = _state.Get<string>(StateKeys.Notify.Text);
        if (text != null)
        {
            var root = (Content as Grid) ?? Content as Panel;
            if (root == null) return;
        for (int i = root.Children.Count - 1; i >= 0; i--)
            if (root.Children[i] is Avalonia.Controls.Label lb && lb.Tag?.ToString() == StateKeys.UiTags.Notify)
                root.Children.RemoveAt(i);
        var notify = new Avalonia.Controls.Label
        {
            Content = text,
            Foreground = Avalonia.Media.Brushes.White,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(180, 0, 0, 0)),
            FontSize = 16,
            Padding = new Avalonia.Thickness(20, 10),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 80, 0, 0),
            Tag = StateKeys.UiTags.Notify,
        };
        root.Children.Add(notify);
        _state.Set(StateKeys.Notify.Text, (string?)null);
        _notifyRemainSeconds = 3.0;
    }
    else if (_notifyRemainSeconds > 0)
    {
        _notifyRemainSeconds -= 0.016;
        if (_notifyRemainSeconds <= 0)
        {
            var root = (Content as Grid) ?? Content as Panel;
            if (root != null)
                for (int i = root.Children.Count - 1; i >= 0; i--)
                    if (root.Children[i] is Avalonia.Controls.Label lb && lb.Tag?.ToString() == StateKeys.UiTags.Notify)
                        root.Children.RemoveAt(i);
        }
    }
    }

    // ========== 动画 ==========

    /// <summary>
    /// 每帧读取 __anim_*_current 状态，更新运行时控件的 Margin/Opacity
    /// </summary>
    private void ApplyAnimations()
    {
        var root = (Content as Grid) ?? Content as Panel;
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
        if (string.IsNullOrEmpty(text)) _dialogBox?.Hide();
        else
        {
            var speaker = _state.Get<string>(StateKeys.Dialog.Speaker);
            _dialogBox?.SetText(text, speaker);
        }
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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Avalonia.Media.Imaging.Bitmap> s_imageCache = new();

    private static double ParseOpacity(Dictionary<string, object> props)
    {
        var s = props.GetValueOrDefault("opacity")?.ToString();
        return s != null && double.TryParse(s, out var op) && op > 0 ? op : 1.0;
    }

    private static void LoadSource(Image img, string source)
    {
        if (s_imageCache.TryGetValue(source, out var cached))
        {
            img.Source = cached;
            return;
        }
        if (source.StartsWith("avares://"))
        {
            try
            {
                var bmp = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri(source)));
                s_imageCache[source] = bmp;
                img.Source = bmp;
            }
            catch { }
        }
        else LoadImageAsync(img, source);
    }

    private static async void LoadImageAsync(Image img, string path)
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
            if (s_imageCache.TryGetValue(fullPath, out var cached))
            {
                img.Source = cached;
                return;
            }
            var bitmap = await Task.Run(() => new Avalonia.Media.Imaging.Bitmap(fullPath));
            s_imageCache[fullPath] = bitmap;
            img.Source = bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SceneView] LoadImage failed: {path} — {ex.Message}");
        }
    }
}
