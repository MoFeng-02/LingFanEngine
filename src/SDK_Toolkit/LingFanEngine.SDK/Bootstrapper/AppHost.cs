using System;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK.Bootstrapper;

/// <summary>
/// 应用主机，封装 IServiceProvider，提供统一服务解析入口
/// </summary>
public sealed class AppHost : IDisposable
{
    private static AppHost? s_current;

    /// <summary>全局当前实例</summary>
    public static AppHost Current =>
        s_current ?? throw new InvalidOperationException("AppHost 尚未构建，请先调用 AppHostBuilder.Build()");

    private readonly IServiceProvider _services;

    internal AppHost(IServiceProvider services)
    {
        _services = services;
        s_current = this;
    }

    /// <summary>底层 IServiceProvider</summary>
    public IServiceProvider Services => _services;

    /// <summary>获取服务（可能为 null）</summary>
    public T? GetService<T>() where T : class => _services.GetService<T>();

    /// <summary>获取必须的服务</summary>
    public T GetRequiredService<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>获取服务（object 版本）</summary>
    public object? GetService(Type serviceType) => _services.GetService(serviceType);

    public void Dispose()
    {
        if (_services is IDisposable disposable)
            disposable.Dispose();
        s_current = null;
    }
}
