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
            options.StoriesDirectory = "Stories";
            options.DesktopTargetFps = 120;
            options.WindowWidth = 1920;
            options.WindowHeight = 1080;
            options.EnableHotReload = true;
            options.ShowPerformanceHud = false;
        });

        // 注册默认命令服务（DSL button cmd="xxx" 依赖此服务）
        services.AddDefaultCommandService();

        // Phase 65: 注册对话框模板
        services.AddDialogTemplates();

        // MainView / MainWindow 不注册到 DI，由 App.cs 手动创建并传入 ServiceProvider
        // （与 Demo Entry 层保持一致，避免 DI 无法解析 ServiceProvider 具体类型）

        return services;
    }

    /// <summary>注册对话框模板到 IDialogTemplateRegistry</summary>
    public static IServiceCollection AddDialogTemplates(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var registry = sp.GetRequiredService<LingFanEngine.Views.IDialogTemplateRegistry>();

            // 底部条状（默认）——引擎层 DialogBox
            registry.Register("bottom", new LingFanEngine.Views.DefaultDialogBoxFactory());

            // 中央气泡——UI 层自定义控件
            registry.Register("center", new _LingFanEngineTemplateTitle_.UI.Dialogs.CenterBubbleDialogBoxFactory());

            // 全屏 NVL——UI 层自定义控件
            registry.Register("fullscreen", new _LingFanEngineTemplateTitle_.UI.Dialogs.FullScreenNvlDialogBoxFactory());

            registry.SetDefault("bottom");
            return registry;
        });
        return services;
    }
}
