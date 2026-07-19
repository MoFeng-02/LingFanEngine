using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Views;

/// <summary>
/// 交互绑定器——为任意控件挂载 nav/cmd/hover/selected/disabled 交互。
/// </summary>
internal sealed class InteractionBinder : IInteractionBinder
{
    private readonly IStateContainer _state;
    private readonly ICommandPipeline _pipeline;
    private readonly ICommandService? _cmdService;

    public InteractionBinder(IStateContainer state, ICommandPipeline pipeline, ICommandService? cmdService = null)
    {
        _state = state;
        _pipeline = pipeline;
        _cmdService = cmdService;
    }

    public void ApplyInteraction(Control control, Dictionary<string, object> props)
    {
        // === disabled / enabled=false：禁用交互 ===
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

        var isSelected = false;

        SolidColorBrush? selectedBrush = selectedColorStr != null
            ? new SolidColorBrush(Color.Parse(selectedColorStr))
            : null;

        // === Button：hover_color + selected_color 联动 ===
        if (control is Button btn)
        {
            var idleBrush = btn.Background as SolidColorBrush;
            var idleColor = idleBrush?.Color ?? Color.FromArgb(100, 80, 80, 80);
            var idleBrushCopy = new SolidColorBrush(idleColor);

            SolidColorBrush hoverBrush;
            if (hoverColorStr != null)
                hoverBrush = new SolidColorBrush(Color.Parse(hoverColorStr));
            else
                hoverBrush = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Min(idleColor.A + 40, 255), idleColor.R, idleColor.G, idleColor.B));

            btn.PointerEntered += (_, _) => btn.Background = hoverBrush;
            btn.PointerExited += (_, _) =>
                btn.Background = isSelected ? (selectedBrush ?? idleBrushCopy) : idleBrushCopy;

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
            control.PointerEntered += (_, _) => ControlFactory.LoadSource(img, hoverSource);
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
        if (!string.IsNullOrEmpty(selectedSource) && control is Image selImg)
        {
            var originalSource = selImg.Source;
            control.PointerPressed += (_, _) =>
            {
                isSelected = !isSelected;
                if (isSelected) ControlFactory.LoadSource(selImg, selectedSource);
                else selImg.Source = originalSource;
            };
        }

        // === 点击行为（nav/cmd）===
        if (!hasClick) return;

        if (props.GetValueOrDefault("cursor") == null)
            control.Cursor = new Cursor(StandardCursorType.Hand);

        if (control is Button clickBtn)
        {
            if (nav != null)
                clickBtn.Click += (_, _) =>
                {
                    _state.Set(StateKeys.Dialog.Complete, false);
                    FireAndForgetSend(new NavigateCommand { Path = nav });
                };
            else if (cmd != null)
            {
                var cmdValue = props.GetValueOrDefault("value")?.ToString();
                clickBtn.Click += (_, _) =>
                {
                    _state.Set(StateKeys.Dialog.Complete, false);
                    if (_cmdService != null)
                        FireAndForgetExecute(cmd, cmdValue);
                };
            }
        }
        else
        {
            if (nav != null)
                control.PointerPressed += (_, _) =>
                {
                    _state.Set(StateKeys.Dialog.Complete, false);
                    FireAndForgetSend(new NavigateCommand { Path = nav });
                };
            else if (cmd != null)
            {
                var cmdValue = props.GetValueOrDefault("value")?.ToString();
                control.PointerPressed += (_, _) =>
                {
                    _state.Set(StateKeys.Dialog.Complete, false);
                    if (_cmdService != null)
                        FireAndForgetExecute(cmd, cmdValue);
                };
            }
        }
    }

    /// <summary>fire-and-forget SendAsync 带异常捕获</summary>
    private void FireAndForgetSend(ICommand cmd)
    {
        _ = _pipeline.SendAsync(cmd).AsTask().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[InteractionBinder] SendAsync failed: {t.Exception?.GetBaseException().Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>fire-and-forget ExecuteAsync 带异常捕获</summary>
    private void FireAndForgetExecute(string cmd, string? cmdValue)
    {
        _ = _cmdService!.ExecuteAsync(cmd, cmdValue).ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[InteractionBinder] ExecuteAsync failed: {t.Exception?.GetBaseException().Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}
