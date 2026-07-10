using System;
using System.Collections.Generic;
using LingFanEngine.SDK.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK.Bootstrapper;

/// <summary>
/// Builder 模式构建 AppHost，支持链式配置
/// </summary>
public sealed class AppHostBuilder
{
    private readonly ServiceCollection _services = new();
    private readonly List<Action<IServiceCollection>> _configureActions = new();
    private bool _sdkRegistered;

    /// <summary>注册服务配置回调</summary>
    public AppHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        _configureActions.Add(configure);
        return this;
    }

    /// <summary>使用 LingFanEngine SDK（注册所有 SDK 服务）</summary>
    public AppHostBuilder UseLingFanEngineSDK()
    {
        _sdkRegistered = true;
        return this;
    }

    /// <summary>构建 AppHost</summary>
    public AppHost Build()
    {
        // 先注册 SDK 核心服务（仅一次）
        if (_sdkRegistered)
            _services.AddLingFanEngineSDK();

        // 执行用户自定义配置
        foreach (var action in _configureActions)
            action(_services);

        var provider = _services.BuildServiceProvider();
        return new AppHost(provider);
    }
}
