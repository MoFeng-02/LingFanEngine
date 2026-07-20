using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace _LingFanEngineTemplateTitle_.Views;

/// <summary>
/// 主窗口——桌面平台入口
/// <para>包装 MainView，提供窗口级功能（标题、尺寸、全屏、键盘快捷键、滚轮回溯）。</para>
/// </summary>
public class MainWindow : Window
{
    private readonly MainView _mainView;

    // F11 全屏切换：记忆切换前的窗口状态以便恢复
    private WindowState _savedRestoreState = WindowState.Normal;

    public MainWindow(MainView mainView)
    {
        _mainView = mainView;

        Title = "灵泛引擎模板";
        Width = 1280;
        Height = 720;
        Background = new SolidColorBrush(Colors.Black);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = _mainView;

        // P1-7: 全屏设置事件订阅
        _mainView.FullscreenChanged += OnFullscreenChanged;

        // 确保窗口可接收键盘事件
        Focusable = true;
        KeyDown += OnKeyDown;
        PointerWheelChanged += async (s, e) => await OnPointerWheelChangedAsync(s, e);

        Closed += async (_, _) =>
        {
            await _mainView.ShutdownAsync();
        };
    }

    /// <summary>全局键盘快捷键</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // F11 切换全屏/窗口模式
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        // 转发给 MainView 处理其他快捷键
        _mainView.HandleKeyDown(e);
    }

    /// <summary>切换全屏/窗口模式</summary>
    public void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _savedRestoreState;
        }
        else
        {
            _savedRestoreState = WindowState;
            WindowState = WindowState.FullScreen;
        }
    }

    /// <summary>全屏设置变更回调（P1-7: SettingsPanel 全屏复选框触发）</summary>
    private void OnFullscreenChanged(bool fullscreen)
    {
        if (fullscreen && WindowState != WindowState.FullScreen)
        {
            _savedRestoreState = WindowState;
            WindowState = WindowState.FullScreen;
        }
        else if (!fullscreen && WindowState == WindowState.FullScreen)
        {
            WindowState = _savedRestoreState;
        }
    }

    /// <summary>鼠标滚轮时间线回溯/前进（Ren'Py 风格）</summary>
    private async Task OnPointerWheelChangedAsync(object? sender, PointerWheelEventArgs e)
    {
        await _mainView.HandlePointerWheelChangedAsync(e);
    }
}
