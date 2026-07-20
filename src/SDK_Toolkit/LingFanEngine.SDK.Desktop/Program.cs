using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LingFanEngine.SDK;
using LingFanEngine.SDK.Desktop.Services;
using LingFanEngine.SDK.Extensions;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK.Desktop;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var services = new ServiceCollection();

        // 注册 SDK 核心服务
        services.AddLingFanEngineSDK();

        // 注册平台服务
        if (OperatingSystem.IsWindows())
            services.AddSingleton<IPlatformService, WindowsPlatformService>();
        else if (OperatingSystem.IsLinux())
            services.AddSingleton<IPlatformService, LinuxPlatformService>();
        else if (OperatingSystem.IsMacOS())
            services.AddSingleton<IPlatformService, MacOSPlatformService>();

        var provider = services.BuildServiceProvider();

        // 应用上次未完成的引擎 DLL pending 更新（SDK 启动最早时机，JIT 加载目标 DLL 前）
        // 无 pending 时立即返回；有 pending 但仍被锁定则保留至下次启动
        try
        {
            var updateService = provider.GetService<IEngineUpdateService>();
            if (updateService != null)
            {
                using var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(10));
                updateService.ApplyPendingUpdatesAsync(cts.Token).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Startup] ApplyPendingUpdates 失败: {ex.Message}");
        }

        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnLastWindowClose,
        };

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
#if DEBUG
    .WithDeveloperTools()   // ← 拦截渲染循环异常，弹可视化弹窗
#endif
            .AfterSetup(_ =>
            {
                var app = Application.Current as App;
                app?.InitializeServices(provider);
            })
            .SetupWithLifetime(lifetime)
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Vulkan, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software] });

        lifetime.Start(args);
    }
}
