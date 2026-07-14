using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK;

/// <summary>
/// Application 子类（AXAML + code-behind）— 暗色 IDE 主题。
/// 管理启动器↔工作台窗口切换。
/// <para>AXAML 中的 StyleInclude 在构建时由 Avalonia XAML 编译器编译为 C# 代码，AOT 安全。</para>
/// </summary>
public partial class App : Application
{
    private IServiceProvider _services = null!;
    private LauncherWindow? _launcher;
    private WorkspaceWindow? _workspace;

    /// <summary>AXAML 加载——构建时编译为 C# 代码，AOT 安全</summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>由 Program.cs 在启动时注入 DI 容器</summary>
    public void InitializeServices(IServiceProvider services)
    {
        _services = services;

        // 监听项目会话——打开→切换到工作台，关闭→切换回启动器
        var session = services.GetRequiredService<IProjectSession>();
        session.ProjectOpened += () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ShowWorkspace);
        };
        session.ProjectClosed += () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ShowLauncher);
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 启动时显示启动器（而非工作台）
            ShowLauncher();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>显示启动器窗口</summary>
    private void ShowLauncher()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // 先创建启动器（确保有窗口存在，避免 OnLastWindowClose 退出）
        _launcher ??= new LauncherWindow(_services.GetRequiredService<LauncherViewModel>());
        desktop.MainWindow = _launcher;
        _launcher.Show();

        // 再关闭工作台
        if (_workspace != null)
        {
            _workspace.Close();
            _workspace = null;
        }
    }

    /// <summary>显示工作台窗口</summary>
    private void ShowWorkspace()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        // 先创建工作台（确保有窗口存在）
        _workspace ??= new WorkspaceWindow(_services);
        desktop.MainWindow = _workspace;
        _workspace.Show();
        _workspace.Activate();

        // 再关闭启动器
        if (_launcher != null)
        {
            _launcher.Close();
            _launcher = null;
        }
    }
}
