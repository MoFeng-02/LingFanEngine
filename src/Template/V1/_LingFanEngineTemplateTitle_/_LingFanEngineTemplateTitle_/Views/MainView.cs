using _LingFanEngineTemplateTitle_.UI;
using Avalonia.Controls;
using Avalonia.Input;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Interfaces.Views;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;
using Microsoft.Extensions.DependencyInjection;

namespace _LingFanEngineTemplateTitle_.Views;

/// <summary>
/// 主视图——引擎内容载体
/// <para>负责 SceneView + OverlayManager 的初始化、GameLoop 启动、DSL 场景导航。</para>
/// <para>桌面平台由 MainWindow 包装，移动/浏览器平台直接作为 MainView 使用。</para>
/// </summary>
public class MainView : UserControl
{
    private readonly ServiceProvider _services;
    private SceneView? _sceneView;
    private OverlayManager? _overlay;
    private GameToolbar? _toolbar;

    // 缓存引用，避免每次滚轮事件都查 DI
    private IStateContainer? _state;
    private IGameController? _gameController;
    private IGameLoop? _gameLoop;
    private LingFanEngine.Services.Scripting.StoryHotReloadService? _hotReload;

    public MainView(ServiceProvider services)
    {
        _services = services;
        StartScreen();
        SetupEngine();
    }

    /// <summary>全屏切换请求事件（P1-7: 由 MainWindow 订阅）</summary>
    public event Action<bool>? FullscreenChanged;

    /// <summary>启动闪屏</summary>
    private void StartScreen()
    {
        var splash = new Grid { Background = Avalonia.Media.Brushes.Black };
        splash.Children.Add(new TextBlock
        {
            Text = "灵泛引擎",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(80, 80, 80, 80)),
            FontSize = 28,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        Content = splash;
    }

    /// <summary>初始化引擎</summary>
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
            _hotReload = _services.GetService<LingFanEngine.Services.Scripting.StoryHotReloadService>();
            _hotReload?.Start();

            var i18n = _services.GetRequiredService<II18nService>();
            var scReg = _services.GetRequiredService<ISceneRegistry>();
            var dialogBoxFactory = _services.GetService<IDialogBoxFactory>();
            var options = _services.GetService<LingFanEngineOptions>() ?? new LingFanEngineOptions();

            // Phase 32: SceneView 模块化——从 DI 获取子模块
            var controlFactory = _services.GetRequiredService<IControlFactory>();
            var interactionBinder = _services.GetRequiredService<IInteractionBinder>();
            var overlayRenderer = _services.GetRequiredService<IOverlayRenderer>();
            var videoPresenter = _services.GetRequiredService<IVideoPresenter>();
            var animationApplier = _services.GetRequiredService<IAnimationApplier>();

            _sceneView = new SceneView(
                state, pipeline, i18n,
                controlFactory, interactionBinder, overlayRenderer, videoPresenter, animationApplier,
                commandService, scReg, dialogBoxFactory,
                options.DesignWidth, options.DesignHeight, options.ScaleMode);

            // 创建覆盖层管理器（存档/设置/历史/快捷菜单）
            var ctrl = _services.GetRequiredService<IGameController>();
            _overlay = new OverlayManager(state, saveService, ctrl);

            // P1-7: 全屏设置事件转发
            _overlay.FullscreenChanged += fullscreen => FullscreenChanged?.Invoke(fullscreen);

            // 创建游戏内工具栏（仅 Game 场景显示）
            _toolbar = new GameToolbar(state, ctrl, _overlay);

            // 使用 Grid 叠加 SceneView、OverlayManager 和工具栏
            var rootGrid = new Grid();
            rootGrid.Children.Add(_sceneView);
            rootGrid.Children.Add(_overlay);
            rootGrid.Children.Add(_toolbar);

            Content = rootGrid;

            // 注册场景视图引用（用于截图）
            if (gameLoop is GameLoop gl)
                gl.SetSceneView((ISceneRenderer)_sceneView);

            // 帧回调
            gameLoop.OnFrame += dt =>
            {
                _sceneView.Update(dt);
                _overlay.Update(dt);
                _toolbar.Update(dt);
            };
            _ = gameLoop.StartAsync();

            // 注册 C# 脚本和命令
            CSharpScripts.RegisterAll(state, scReg, ctrl, pipeline, commandService, audio, gameLoop as GameLoop, _overlay, saveService);

            // 启动到 DSL 标题场景
            var titleScene = options.TitleSceneName;
            pipeline.SendAsync(new NavigateCommand { Path = titleScene });
            // 立即触发首帧渲染，消除黑屏延迟
            _sceneView.Update(0.016);

            _state = state;
            _gameController = ctrl;
            _gameLoop = gameLoop;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainView] 初始化错误: {ex}");
            throw;
        }
    }

    /// <summary>键盘快捷键处理（由 MainWindow 转发）</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        _overlay?.HandleKeyDown(e);
    }

    /// <summary>鼠标滚轮回溯/前进处理（由 MainWindow 转发）</summary>
    public async Task HandlePointerWheelChangedAsync(PointerWheelEventArgs e)
    {
        if (_state == null || _gameController == null) return;

        // 仅 Game 场景支持滚轮回溯
        if ((SceneType)_state.Get<int>(StateKeys.Scene.CurrentType) != SceneType.Game)
            return;

        // 节流：50ms
        var now = Environment.TickCount64;
        if (now - _lastWheelTicks < 50) return;

        if (e.Delta.Y > 0)
        {
            _lastWheelTicks = now;
            await _gameController.RollbackAsync();
            e.Handled = true;
        }
        else if (e.Delta.Y < 0)
        {
            _lastWheelTicks = now;
            await _gameController.RollforwardAsync();
            e.Handled = true;
        }
    }

    private long _lastWheelTicks;

    /// <summary>关闭时清理资源</summary>
    public async Task ShutdownAsync()
    {
        _hotReload?.Stop();
        if (_gameLoop != null)
            await _gameLoop.StopAsync();
        try
        {
            await _services.DisposeAsync();
        }
        catch { }
    }
}
