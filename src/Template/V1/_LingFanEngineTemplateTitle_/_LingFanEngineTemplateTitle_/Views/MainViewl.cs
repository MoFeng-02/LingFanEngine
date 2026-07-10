using _LingFanEngineTemplateTitle_.UI;
using Avalonia.Controls;
using Avalonia.Media;
using LibVLCSharp.Shared;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;

namespace _LingFanEngineTemplateTitle_.Views;

public class MainView : UserControl
{
    private LingFanEngine.Views.SceneView? _sceneView;
    private UI.OverlayManager? _overlay;

    // 缓存引用，避免每次滚轮事件都查 DI
    private readonly IStateContainer? _state;
    private readonly IGameController? _gameController;
    private readonly ICommandService? commandService;
    private readonly IAudioManager? _audioManager;
    private readonly ISaveService? _saveService;

    public MainView(IStateContainer? state, IGameController? gameController, ICommandService? commandService, IAudioManager? audioManager, ISaveService? saveService)
    {
        _state = state;
        _gameController = gameController;
        this.commandService = commandService;
        _audioManager = audioManager;
        _saveService = saveService;
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
            _sceneView = new LingFanEngine.Views.SceneView(state, pipeline, i18n, commandService, scReg, dialogBoxFactory,
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
            if (gameLoop is GameLoop gl) gl.SetSceneView(_sceneView);

            gameLoop.OnFrame += dt =>
            {
                _sceneView.Update(dt);
                _overlay.Update(dt);
            };
            _ = gameLoop.StartAsync();
            //Closed += async (_, _) =>
            //{
            //    hotReload?.Stop();
            //    await gameLoop.StopAsync();
            //    await _services.DisposeAsync();
            //};

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


}