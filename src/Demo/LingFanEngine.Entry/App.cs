using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LingFanEngine.Entry.Views;
using LingFanEngine.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.Entry;

public partial class App : Application
{
    /// <summary>
    /// 全局 DI 服务提供器
    /// </summary>
    public static ServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        //AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());
        // 构建 DI 容器
        var services = new ServiceCollection();

        // 注册引擎核心（包含所有运行时服务）
        services.AddLingFanEngine(options =>
        {
            options.SaveDirectory = "Saves";
            options.MediaDirectory = "Media";
            options.Live2DDirectory = "Live2D";
            options.ModsDirectory = "Mods";
            options.StoriesDirectory = "Stories";
            options.DesktopTargetFps = 120;
            options.WindowWidth = 1920;
            options.WindowHeight = 1080;
            options.EnableHotReload = true;
            options.ShowPerformanceHud = true;
        });

        // 注册默认命令服务
        services.AddDefaultCommandService();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(Services);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var control = new MainWindow(Services);
            singleView.MainView = control;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () => new MainWindow(Services);
        }

        base.OnFrameworkInitializationCompleted();
    }
}