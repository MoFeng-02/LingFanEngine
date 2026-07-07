using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LingFanEngine.Abstractions.EngineOptions;
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

    public MainWindow(ServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        SetupEngine();
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

            Title = "灵泛引擎 — 入门教程";
            Width = 1280;
            Height = 720;
            Background = new SolidColorBrush(Colors.Black);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var i18n = _services.GetRequiredService<II18nService>();
            var scReg = _services.GetRequiredService<ISceneRegistry>();
            _sceneView = new LingFanEngine.Views.SceneView(state, pipeline, i18n, commandService, scReg);

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
            Closed += async (_, _) => { await gameLoop.StopAsync(); await _services.DisposeAsync(); };

            CSharpScripts.RegisterAll(state, scReg, ctrl, pipeline, commandService, audio, gameLoop as GameLoop);
            // 启动到 DSL 标题场景（.story 文件由 StoryRegistry 扫描并懒加载）
            var titleScene = _services.GetRequiredService<LingFanEngineOptions>().TitleSceneName;
            pipeline.SendAsync(new NavigateCommand { Path = titleScene });
            // 立即触发首帧渲染，不等 GameLoop 帧回调（消除构造函数结束到首帧之间的黑屏延迟）
            _sceneView.Update(0.016);

            // 确保窗口可接收键盘事件
            Focusable = true;
            KeyDown += OnKeyDown;
        }
        catch (Exception ex) { Console.WriteLine($"初始化错误: {ex.Message}"); throw; }
    }

    /// <summary>全局键盘快捷键</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // 直接调用覆盖层管理器的键盘处理方法
        // 注意：不能使用 RaiseEvent，因为 OverlayManager 是 Window 的子控件，
        // RaiseEvent 会让 KeyDown 事件冒泡回 Window，导致无限递归 (StackOverflow)
        _overlay?.HandleKeyDown(e);
    }
}

