﻿﻿using System;
using System.Net.Http.Headers;
using LingFanEngine.SDK.AI;
using LingFanEngine.SDK.Constants;
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
        services.AddSingleton<IResourceEncryptor, ResourceEncryptor>();
        services.AddSingleton<IFileEditor, FileEditor>();
        services.AddSingleton<ITranslationService, TranslationService>();
        services.AddSingleton<IModelService, ModelService>();
        services.AddSingleton<IModelClientFactory, ModelClientFactory>();
        services.AddSingleton<ITranslatorFactory, TranslatorFactory>();
        services.AddSingleton<ModelConnectivityService>();
        services.AddSingleton<IPackToolService, PackToolService>();
        services.AddSingleton<IPublishService>(sp => new PublishService(
            sp.GetRequiredService<IPackToolService>()));
        services.AddSingleton<IAssetManager, AssetManager>();

        // ===== 远端更新客户端（模板更新；引擎自 2026-09 起经 NuGet 分发，SDK 不再做引擎 DLL 热更） =====
        // 通过 IHttpClientFactory 注册命名客户端，由工厂管理 handler 池，避免套接字耗尽。
        // SDK 版本作为 User-Agent 后缀，便于 GitHub 侧识别客户端。
        var sdkVersion = typeof(ServiceCollectionExtensions).Assembly
            .GetName().Version?.ToString(3) ?? "0.0.0";
        services.AddHttpClient(TemplateDefaults.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(TemplateDefaults.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"{TemplateDefaults.UserAgent}/{sdkVersion}");
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // ===== 模板更新服务（从 Release 拉取模板 zip 并做版本管理） =====
        services.AddSingleton<ITemplateUpdateService, TemplateUpdateService>();

        // ===== 游戏运行服务（启动/停止用户已构建的游戏） =====
        services.AddSingleton<IRunService, RunService>();

        // ===== 路由（MFToolkit.Routing） =====
        // AddRoutes 自动将 RouteType 和 ViewModelType 注册到 DI 容器（Transient）
        services.AddRouting();
        services.AddRoutes(RouteDefinitions.GetAllRoutes());

        // ViewModel 注册为 Singleton（覆盖 AddRoutes 的 Transient 注册）
        // 确保 Page 构造函数注入、Router 创建、侧面板 GetService 获取的是同一实例
        services.AddSingleton<AssetManagerViewModel>();
        services.AddSingleton<TranslationViewModel>();
        services.AddSingleton<ModelsViewModel>();
        services.AddSingleton<BuildViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // ===== 启动器 ViewModel =====
        services.AddTransient<LauncherViewModel>();

        // IPlatformService 不在这里注册——由平台项目注册
        return services;
    }
}
