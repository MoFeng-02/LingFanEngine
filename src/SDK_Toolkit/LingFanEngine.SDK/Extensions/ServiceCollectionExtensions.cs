﻿﻿﻿﻿using System;
using LingFanEngine.SDK.Navigation;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;
using LingFanEngine.SDK.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MFToolkit.Routing.DependencyInjection;

namespace LingFanEngine.SDK.Extensions;

/// <summary>
/// SDK 统一服务注册入口
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 LingFanEngine SDK 所有服务
    /// </summary>
    public static IServiceCollection AddLingFanEngineSDK(this IServiceCollection services)
    {
        // ===== 服务层（Singleton） =====
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IProjectSession>(sp => new ProjectSession(
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<ITemplateService>()));
        services.AddSingleton<ITemplateService, TemplateService>();
        services.AddSingleton<IDslAnalyzer, DslAnalyzer>();
        services.AddSingleton<IResourceEncryptor, ResourceEncryptor>();
        services.AddSingleton<IPackToolService, PackToolService>();
        services.AddSingleton<IPublishService>(sp => new PublishService(
            sp.GetRequiredService<IPackToolService>()));
        services.AddSingleton<IAssetManager, AssetManager>();

        // ===== 路由（MFToolkit.Routing） =====
        // AddRoutes 自动将 RouteType 和 ViewModelType 注册到 DI 容器（Transient）
        services.AddRouting();
        services.AddRoutes(RouteDefinitions.GetAllRoutes());

        // ViewModel 注册为 Singleton（覆盖 AddRoutes 的 Transient 注册）
        // 确保 Page 构造函数注入、Router 创建、侧面板 GetService 获取的是同一实例
        services.AddSingleton<StoryEditorViewModel>();
        services.AddSingleton<AssetManagerViewModel>();
        services.AddSingleton<BuildViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // ===== 启动器 ViewModel =====
        services.AddTransient<LauncherViewModel>();

        // IPlatformService 不在这里注册——由平台项目注册
        return services;
    }
}
