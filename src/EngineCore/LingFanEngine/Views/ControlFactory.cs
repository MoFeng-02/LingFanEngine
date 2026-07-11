using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Views;

/// <summary>
/// 控件工厂——将 UIElementEntity 转换为 Avalonia 控件，并应用布局与通用属性。
/// </summary>
internal sealed class ControlFactory : IControlFactory
{
    private readonly II18nService _i18n;
    private readonly IStateContainer _state;

    /// <summary>绑定表达式的 TextBlock 列表（含原始文本，用于变量变化时刷新）</summary>
    private readonly List<(TextBlock tb, string rawText)> _boundTextBlocks = new();

    /// <summary>获取绑定表达式的 TextBlock 列表（只读视图）</summary>
    public IReadOnlyList<(TextBlock tb, string rawText)> BoundTextBlocks => _boundTextBlocks;

    /// <summary>LRU 图片缓存——限制上限并自动 Dispose 旧 Bitmap，防止长时间运行 OOM</summary>
    private static readonly LruImageCache s_imageCache = new(128);

    public ControlFactory(II18nService i18n, IStateContainer state)
    {
        _i18n = i18n;
        _state = state;
    }

    /// <summary>清空绑定文本块列表（RebuildScene 开始时调用）</summary>
    public void ClearBoundTextBlocks() => _boundTextBlocks.Clear();

    /// <summary>刷新所有绑定表达式的 TextBlock（变量变化时重新求值）</summary>
    public void RefreshBoundTextBlocks()
    {
        foreach (var (tb, raw) in _boundTextBlocks)
        {
            var replaced = ExpressionParser.Replace(_i18n.Translate(raw), _state);
            if (tb.Text != replaced) tb.Text = replaced;
        }
    }

    // ========== ConvertToControl ==========

    public Control? ConvertToControl(UIElementEntity element, double parentW = 1280, double parentH = 720, string layoutMode = "grid")
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
                    VerticalAlignment = VerticalAlignment.Top
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
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                var btn = new Button
                {
                    Content = btnText,
                    Background = new SolidColorBrush(bgColor),
                    BorderThickness = new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand)
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
                if (type == "vbar") bar.Orientation = Orientation.Vertical;
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
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
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

                var stretch = Stretch.Uniform;
                var stretchStr = props.GetValueOrDefault("stretch")?.ToString()?.ToLowerInvariant();
                if (stretchStr != null)
                {
                    stretch = stretchStr switch
                    {
                        "fill" => Stretch.Fill,
                        "uniformtofill" or "tofill" => Stretch.UniformToFill,
                        _ => Stretch.Uniform
                    };
                }
                else
                {
                    var wIsFullPct = props.TryGetValue("width", out var wVal) && wVal?.ToString() == "100%";
                    var hIsFullPct = props.TryGetValue("height", out var hVal) && hVal?.ToString() == "100%";
                    if (wIsFullPct && hIsFullPct)
                        stretch = Stretch.UniformToFill;
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
                            ? Orientation.Horizontal
                            : Orientation.Vertical
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

            case "stack" or "stackpanel":
            {
                var direction = props.GetValueOrDefault("direction")?.ToString()?.ToLowerInvariant() ?? "vertical";
                var stack = new StackPanel
                {
                    Orientation = direction == "horizontal"
                        ? Orientation.Horizontal
                        : Orientation.Vertical
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

            case "scroll" or "scrollviewer":
            {
                var scroll = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
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

            case "viewport":
            {
                var scroll = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = props.TryGetValue("scroll_h", out var sh) && sh?.ToString() == "true"
                        ? ScrollBarVisibility.Auto
                        : ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = props.TryGetValue("scroll_v", out var sv) && sv?.ToString() == "false"
                        ? ScrollBarVisibility.Disabled
                        : ScrollBarVisibility.Auto
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

            case "slider":
            {
                var min = LayoutHelper.ParseDouble(props, "min");
                var max = LayoutHelper.ParseDouble(props, "max");
                var val = LayoutHelper.ParseDouble(props, "value");
                var slider = new Slider
                {
                    Minimum = min,
                    Maximum = max > min ? max : 100,
                    Value = Math.Clamp(val, min, max > min ? max : 100)
                };
                if (props.TryGetValue("orientation", out var orient))
                    slider.Orientation = orient?.ToString()?.Equals("vertical", StringComparison.OrdinalIgnoreCase) == true
                        ? Orientation.Vertical
                        : Orientation.Horizontal;
                return slider;
            }

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
                            var cx = LayoutHelper.ResolveSize(child.Properties, "x", parentW);
                            var cy = LayoutHelper.ResolveSize(child.Properties, "y", parentH);
                            if (cx > 0) Canvas.SetLeft(childControl, cx);
                            if (cy > 0) Canvas.SetTop(childControl, cy);
                            canvas.Children.Add(childControl);
                        }
                    }
                }
                return canvas;
            }

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

            case "separator":
            {
                return new Separator
                {
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255))
                };
            }

            case "spacer":
            {
                var w = LayoutHelper.ResolveSize(props, "width", parentW);
                var h = LayoutHelper.ResolveSize(props, "height", parentH);
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
                    Volume = 0,
                };

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

    // ========== ApplyLayout ==========

    public void ApplyLayout(Control control, Dictionary<string, object> props, double pw, double ph,
        string layoutMode = "grid")
    {
        // === Grid 附着属性 ===
        var col = LayoutHelper.ParseInt(props, "col");
        if (col.HasValue) Grid.SetColumn(control, col.Value);
        var row = LayoutHelper.ParseInt(props, "row");
        if (row.HasValue) Grid.SetRow(control, row.Value);
        var colspan = LayoutHelper.ParseInt(props, "colspan");
        if (colspan.HasValue) Grid.SetColumnSpan(control, colspan.Value);
        var rowspan = LayoutHelper.ParseInt(props, "rowspan");
        if (rowspan.HasValue) Grid.SetRowSpan(control, rowspan.Value);

        // === 尺寸 ===
        var widthStr = props.GetValueOrDefault("width")?.ToString();
        var heightStr = props.GetValueOrDefault("height")?.ToString();

        if (widthStr != null)
        {
            if (widthStr.TrimEnd('%') == "100" || widthStr == "*")
            {
                if (control.HorizontalAlignment == HorizontalAlignment.Stretch)
                    { }
                else if (!props.ContainsKey("halign") && !props.ContainsKey("align") && !props.ContainsKey("xalign"))
                    control.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
            else if (widthStr.EndsWith('%'))
            {
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
                    control.VerticalAlignment = VerticalAlignment.Stretch;
            }
            else if (heightStr.EndsWith('%'))
            {
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

        // MinWidth / MinHeight / MaxWidth / MaxHeight
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

            ApplyCommonProps(control, props);
            return;
        }

        // === Grid/Panel/Stack 相对定位模式 ===
        var halignKey = props.ContainsKey("halign") ? "halign"
            : props.ContainsKey("align") ? "align"
            : props.ContainsKey("xalign") ? "xalign" : null;

        if (halignKey != null)
        {
            control.HorizontalAlignment = props[halignKey]?.ToString()?.ToLowerInvariant() switch
            {
                "center" => HorizontalAlignment.Center,
                "right" => HorizontalAlignment.Right,
                "stretch" => HorizontalAlignment.Stretch,
                _ => HorizontalAlignment.Left
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
                "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                "stretch" => VerticalAlignment.Stretch,
                _ => VerticalAlignment.Top
            };
        }

        // === 位置：百分比/像素 → Margin ===
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

                var xanchor = LayoutHelper.ParseDouble(props, "xanchor");

                if (xanchor > 0 && !double.IsNaN(control.Width))
                {
                    marginLeft = xPx - control.Width * xanchor;
                }
                else if (control.HorizontalAlignment == HorizontalAlignment.Center)
                {
                    marginLeft = 2 * xPx - pw;
                }
                else if (control.HorizontalAlignment == HorizontalAlignment.Right)
                {
                    marginRight = pw - xPx;
                    hasRight = true;
                }
                else
                {
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
                else if (control.VerticalAlignment == VerticalAlignment.Center)
                {
                    marginTop = 2 * yPx - ph;
                }
                else if (control.VerticalAlignment == VerticalAlignment.Bottom)
                {
                    marginBottom = ph - yPx;
                    hasBottom = true;
                }
                else
                {
                    marginTop = yPx;
                }
            }

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

        ApplyCommonProps(control, props);
    }

    // ========== ApplyCommonProps ==========

    public void ApplyCommonProps(Control control, Dictionary<string, object> props)
    {
        // === Padding ===
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

        // === Opacity ===
        var opacity = LayoutHelper.ParseDouble(props, "opacity");
        if (opacity >= 0) control.Opacity = Math.Clamp(opacity, 0, 1);

        // === IsVisible ===
        if (props.TryGetValue("visible", out var visVal))
        {
            control.IsVisible = visVal switch
            {
                bool b => b,
                string s => !s.Equals("false", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        // === IsEnabled ===
        if (props.TryGetValue("enabled", out var enVal))
        {
            control.IsEnabled = enVal switch
            {
                bool b => b,
                string s => !s.Equals("false", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        // === ZIndex ===
        var zindex = LayoutHelper.ParseInt(props, "zindex");
        if (zindex.HasValue) control.SetValue(Avalonia.Controls.Panel.ZIndexProperty, zindex.Value);

        // === ClipToBounds ===
        if (props.TryGetValue("clipToBounds", out var clipVal))
        {
            control.ClipToBounds = clipVal switch
            {
                bool b => b,
                string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        // === Cursor ===
        var cursorStr = props.GetValueOrDefault("cursor")?.ToString()?.ToLowerInvariant();
        if (cursorStr != null)
        {
            control.Cursor = cursorStr switch
            {
                "hand" or "pointer" => new Cursor(StandardCursorType.Hand),
                "text" or "ibeam" => new Cursor(StandardCursorType.Ibeam),
                "wait" or "loading" => new Cursor(StandardCursorType.Wait),
                "cross" or "crosshair" => new Cursor(StandardCursorType.Cross),
                "help" => new Cursor(StandardCursorType.Help),
                "no" or "forbidden" => new Cursor(StandardCursorType.No),
                _ => new Cursor(StandardCursorType.Arrow)
            };
        }

        // === RenderTransform (rotation / scale) ===
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

        // === Border 专属 ===
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

        // === StackPanel 专属 ===
        if (control is StackPanel stack)
        {
            var spacing = LayoutHelper.ParseDouble(props, "spacing");
            if (spacing > 0) stack.Spacing = spacing;
        }
    }

    // ========== 图片加载 ==========

    internal static void LoadSource(Image img, string source)
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ControlFactory] LoadSource(avares) failed: {source} — {ex.Message}"); }
        }
        else _ = LoadImageAsync(img, source);
    }

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
            System.Diagnostics.Debug.WriteLine($"[ControlFactory] LoadImage failed: {path} — {ex.Message}");
        }
    }

    private static double ParseOpacity(Dictionary<string, object> props)
    {
        var s = props.GetValueOrDefault("opacity")?.ToString();
        return s != null && double.TryParse(s, out var op) && op > 0 ? op : 1.0;
    }

    // ========== 表达式替换 ==========

    private string ReplaceExpressions(string text) => ExpressionParser.Replace(text, _state);
}
