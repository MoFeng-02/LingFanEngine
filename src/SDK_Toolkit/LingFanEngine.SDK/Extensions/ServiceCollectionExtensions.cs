﻿using System;
using LingFanEngine.SDK.Navigation;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Services.Implementations;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views.Pages;
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
        services.AddSingleton<IPublishService, PublishService>();
        services.AddSingleton<IAssetManager, AssetManager>();

        // ===== 路由（MFToolkit.Routing） =====
        services.AddRouting();
        services.AddRoutes(RouteDefinitions.GetAllRoutes());

        // ===== ViewModels（Transient） =====
        services.AddTransient<LauncherViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ProjectViewModel>();
        services.AddTransient<StoryEditorViewModel>();
        services.AddTransient<AssetManagerViewModel>();
        services.AddTransient<BuildViewModel>();
        services.AddTransient<SettingsViewModel>();

        // ===== 页面（Transient） =====
        services.AddTransient<StoryEditorPage>();
        services.AddTransient<AssetManagerPage>();
        services.AddTransient<BuildPage>();
        services.AddTransient<SettingsPage>();

        // IPlatformService 不在这里注册——由平台项目注册
        return services;
    }
}
