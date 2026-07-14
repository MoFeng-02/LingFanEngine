using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Extensions;
using _LingFanEngineTemplateTitle_.Security;
using Microsoft.Extensions.DependencyInjection;

namespace _LingFanEngineTemplateTitle_.Extensions;

public static class ServiceExtensions
{
    /// <summary>
    /// 注册游戏服务
    /// </summary>
    public static IServiceCollection AddGameService(this IServiceCollection services)
    {
        // 注册加密密钥提供者（必须在 AddLingFanEngine 之前，引擎的 TryAddSingleton 会跳过 NullEncryptionKeyProvider）
        // GeneratedKeys.cs 在构建加密时由 KeyInjector 生成真实密钥；开发期占位返回 null
        services.AddSingleton<IEncryptionKeyProvider, GeneratedKeyProvider>();

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
            options.ShowPerformanceHud = false;
        });

        // 注册默认命令服务（DSL button cmd="xxx" 依赖此服务）
        services.AddDefaultCommandService();

        // MainView / MainWindow 不注册到 DI，由 App.cs 手动创建并传入 ServiceProvider
        // （与 Demo Entry 层保持一致，避免 DI 无法解析 ServiceProvider 具体类型）

        return services;
    }
}
