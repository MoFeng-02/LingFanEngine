using _LingFanEngineTemplateTitle_.Extensions;
using _LingFanEngineTemplateTitle_.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;

namespace _LingFanEngineTemplateTitle_;

public class App : Application
{
    /// <summary>
    /// 全局 DI 服务提供器
    /// </summary>
    public static ServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Styles.Add(new FluentTheme());

        // 构建 DI 容器
        var services = new ServiceCollection();
        services.AddGameService();
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(new MainView(Services));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView(Services);
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () => new MainView(Services!);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
