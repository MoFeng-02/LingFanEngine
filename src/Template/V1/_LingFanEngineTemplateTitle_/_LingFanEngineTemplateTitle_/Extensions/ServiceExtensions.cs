using _LingFanEngineTemplateTitle_.Views;
using LingFanEngine.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace _LingFanEngineTemplateTitle_.Extensions;

public static class ServiceExtensions
{
    /// <summary>
    /// 注册游戏
    /// </summary>
    /// <returns></returns>
    public static IServiceCollection AddGameService(this IServiceCollection services)
    {
        services.AddSingleton<MainView>();
        
        services.AddSingleton<MainWindow>();

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

        return services;
    }
}
