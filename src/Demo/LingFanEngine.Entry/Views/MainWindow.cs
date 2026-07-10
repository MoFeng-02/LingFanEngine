using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Services.Core;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.Entry.Views;

public partial class MainWindow : Window
{
    private readonly ServiceProvider _services;
    private LingFanEngine.Views.SceneView? _sceneView;
    private Entry.UI.OverlayManager? _overlay;

    // 鼠标滚轮节流：防止高精度滚轮连续触发回溯/前进
    private long _lastWheelTicks;
    // 缓存引用，避免每次滚轮事件都查 DI
    private IStateContainer? _state;
    private IGameController? _gameController;

    // F11 全屏切换：记忆切换前的窗口状态以便恢复
    private WindowState _savedRestoreState = WindowState.Normal;

    public MainWindow(ServiceProvider services)
    {
        _services = services;
        //InitializeComponent();
        StartScreen();
        SetupEngine();
    }

    private void StartScreen()
    {
        var splash = new Grid { Background = Brushes.Black };
        splash.Children.Add(new TextBlock
        {
            Text = "灵泛引擎",
            Foreground = new SolidColorBrush(Color.FromArgb(80, 80, 80, 80)),
            FontSize = 28,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        Content = splash;
    }

    private void SetupEngine()
    {
        try
        {
            var state = _services.GetRequiredService<IStateContainer>();
            var pipeline = _services.GetRequiredService<ICommandPipeline>();
            var gameLoop = _services.GetRequiredService<IGameLoop>();
            var commandService = _services.GetService<ICommandService>();
            var audio = _services.GetService<IAudioManager>();
            var saveService = _services.GetService<ISaveService>();

            // 启动故事热重载（如果 EnableHotReload=true）
            var hotReload = _services.GetService<LingFanEngine.Services.Scripting.StoryHotReloadService>();
            hotReload?.Start();

            Title = "灵泛引擎 — 入门教程";
            Width = 1280;
            Height = 720;
            Background = new SolidColorBrush(Colors.Black);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

                        var i18n = _services.GetRequiredService<II18nService>();
            var scReg = _services.GetRequiredService<ISceneRegistry>();
            var dialogBoxFactory = _services.GetService<LingFanEngine.Views.IDialogBoxFactory>();
            var options = _services.GetService<LingFanEngineOptions>() ?? new LingFanEngineOptions();

            // Phase 32: SceneView 模块化——从 DI 获取子模块
            var controlFactory = _services.GetRequiredService<LingFanEngine.Views.IControlFactory>();
            var interactionBinder = _services.GetRequiredService<LingFanEngine.Views.IInteractionBinder>();
            var overlayRenderer = _services.GetRequiredService<LingFanEngine.Views.IOverlayRenderer>();
            var videoPresenter = _services.GetRequiredService<LingFanEngine.Views.IVideoPresenter>();
            var animationApplier = _services.GetRequiredService<LingFanEngine.Views.IAnimationApplier>();

            _sceneView = new LingFanEngine.Views.SceneView(
                state, pipeline, i18n,
                controlFactory, interactionBinder, overlayRenderer, videoPresenter, animationApplier,
                commandService, scReg, dialogBoxFactory,
                options.DesignWidth, options.DesignHeight, options.ScaleMode);

            // 创建覆盖层管理器（存档/设置/历史/快捷菜单）
            var ctrl = _services.GetRequiredService<IGameController>();
            _overlay = new Entry.UI.OverlayManager(state, saveService, ctrl);

            // 使用 Grid 叠加 SceneView 和 OverlayManager
            var rootGrid = new Grid();
            rootGrid.Children.Add(_sceneView);
            rootGrid.Children.Add(_overlay);

            Content = rootGrid;

            // 注册场景视图引用（用于截图）
            if (gameLoop is GameLoop gl) gl.SetSceneView((LingFanEngine.Views.ISceneRenderer)_sceneView);

            gameLoop.OnFrame += dt =>
            {
                _sceneView.Update(dt);
                _overlay.Update(dt);
            };
            _ = gameLoop.StartAsync();
            Closed += async (_, _) =>
            {
                hotReload?.Stop();
                await gameLoop.StopAsync();
                await _services.DisposeAsync();
            };

            CSharpScripts.RegisterAll(state, scReg, ctrl, pipeline, commandService, audio, gameLoop as GameLoop, _overlay, saveService);
            // 启动到 DSL 标题场景（.story 文件由 StoryRegistry 扫描并懒加载）
            var titleScene = _services.GetRequiredService<LingFanEngineOptions>().TitleSceneName;
            pipeline.SendAsync(new NavigateCommand { Path = titleScene });
            // 立即触发首帧渲染，不等 GameLoop 帧回调（消除构造函数结束到首帧之间的黑屏延迟）
            _sceneView.Update(0.016);

            // 确保窗口可接收键盘事件
            Focusable = true;
            KeyDown += OnKeyDown;

            // 鼠标滚轮：Ren'Py 风格回溯/前进
            // 滚轮向上 = 回退（rollback），滚轮向下 = 前进（rollforward）
            _state = state;
            _gameController = ctrl;
            PointerWheelChanged += async (s, e) => await OnPointerWheelChangedAsync(s, e);
        }
        catch (Exception ex) { Console.WriteLine($"初始化错误: {ex.Message}"); throw; }
    }

    /// <summary>全局键盘快捷键</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // F11 切换全屏/窗口模式（对标大多数游戏和浏览器行为）
        if (e.Key == Avalonia.Input.Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        // 直接调用覆盖层管理器的键盘处理方法
        // 注意：不能使用 RaiseEvent，因为 OverlayManager 是 Window 的子控件，
        // RaiseEvent 会让 KeyDown 事件冒泡回 Window，导致无限递归 (StackOverflow)
        _overlay?.HandleKeyDown(e);
    }

    /// <summary>
    /// 切换全屏/窗口模式
    /// <para>全屏时使用 WindowState.FullScreen（独占全屏，无边框无标题栏）</para>
    /// <para>恢复时还原到切换前的窗口状态（Normal/Maximized）</para>
    /// </summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            // 恢复到切换前的状态
            WindowState = _savedRestoreState;
        }
        else
        {
            // 记忆当前状态用于恢复
            _savedRestoreState = WindowState;
            WindowState = WindowState.FullScreen;
        }
    }

    /// <summary>
    /// 鼠标滚轮时间线回溯/前进（Ren'Py 风格）
    /// <para>滚轮向上 = 时间线回退，滚轮向下 = 时间线前进</para>
    /// <para>仅 Game 场景生效（Menu/UI 场景不创建回溯检查点）</para>
    /// </summary>
    private async Task OnPointerWheelChangedAsync(object? sender, PointerWheelEventArgs e)
    {
        if (_state == null || _gameController == null) return;

        // 仅 Game 场景支持滚轮回溯
        if ((SceneType)_state.Get<int>(StateKeys.Scene.CurrentType) != SceneType.Game)
            return;

        // 节流：高精度滚轮一次拨动可能产生多个事件
        var now = Environment.TickCount64;
        if (now - _lastWheelTicks < 50) return;

        if (e.Delta.Y > 0)
        {
            // 滚轮向上 = 时间线回退
            _lastWheelTicks = now;
            await _gameController.RollbackAsync();
            e.Handled = true;
        }
        else if (e.Delta.Y < 0)
        {
            // 滚轮向下 = 时间线前进
            _lastWheelTicks = now;
            await _gameController.RollforwardAsync();
            e.Handled = true;
        }
    }
}

