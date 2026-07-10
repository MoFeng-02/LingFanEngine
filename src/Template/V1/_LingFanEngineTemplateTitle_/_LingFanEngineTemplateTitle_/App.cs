using _LingFanEngineTemplateTitle_.Extensions;
using _LingFanEngineTemplateTitle_.ViewModels;
using _LingFanEngineTemplateTitle_.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;

namespace _LingFanEngineTemplateTitle_;

public class App : Application
{
    public override void Initialize()
    {

    }

    public override void OnFrameworkInitializationCompleted()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddGameService();

        var provider = services.BuildServiceProvider();



        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = provider.GetRequiredService<MainWindow>();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () => provider.GetRequiredService<MainView>();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = provider.GetRequiredService<MainView>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}