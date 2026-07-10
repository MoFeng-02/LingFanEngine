using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.ViewModels;
using LingFanEngine.SDK.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LingFanEngine.SDK;

/// <summary>
/// 纯 C# Application 子类（无 .axaml）— 暗色 IDE 主题
/// 管理启动器↔工作台窗口切换。
/// </summary>
public class App : Application
{
    private IServiceProvider _services = null!;
    private LauncherWindow? _launcher;
    private WorkspaceWindow? _workspace;

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

    public override void Initialize()
    {
        // 纯 C# 模式，无需 AvaloniaXamlLoader
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var fluentTheme = new FluentTheme();
        Styles.Add(fluentTheme);

        // 设置全局暗色主题
        RequestedThemeVariant = ThemeVariant.Dark;

        RegisterGlobalStyles();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ★ 启动时显示启动器（而非工作台）
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

    private void RegisterGlobalStyles()
    {
        // 暗色背景
        Styles.Add(new Style(f => f.OfType<Window>())
        {
            Setters =
            {
                new Setter(Window.BackgroundProperty, new SolidColorBrush(Color.Parse("#1E1E1E"))),
                new Setter(Window.ForegroundProperty, new SolidColorBrush(Color.Parse("#D4D4D4"))),
            }
        });

        // 导航按钮统一样式
        Styles.Add(new Style(f => f.OfType<Button>().Class("nav-button"))
        {
            Setters =
            {
                new Setter(Button.HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
                new Setter(Button.PaddingProperty, new Thickness(16, 10)),
                new Setter(Button.MarginProperty, new Thickness(0, 2)),
            }
        });

        // 页面标题样式（暗色）
        Styles.Add(new Style(f => f.OfType<TextBlock>().Class("page-title"))
        {
            Setters =
            {
                new Setter(TextBlock.FontSizeProperty, 20.0),
                new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold),
                new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 0, 16)),
                new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.Parse("#FFFFFF"))),
            }
        });

        // TextBox 暗色
        Styles.Add(new Style(f => f.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.BackgroundProperty, new SolidColorBrush(Color.Parse("#2D2D30"))),
                new Setter(TextBox.ForegroundProperty, new SolidColorBrush(Color.Parse("#D4D4D4"))),
                new Setter(TextBox.BorderBrushProperty, new SolidColorBrush(Color.Parse("#3C3C3C"))),
            }
        });

        // ListBox 暗色
        Styles.Add(new Style(f => f.OfType<ListBox>())
        {
            Setters =
            {
                new Setter(ListBox.BackgroundProperty, new SolidColorBrush(Color.Parse("#252526"))),
                new Setter(ListBox.ForegroundProperty, new SolidColorBrush(Color.Parse("#D4D4D4"))),
                new Setter(ListBox.BorderBrushProperty, new SolidColorBrush(Color.Parse("#3C3C3C"))),
            }
        });

        // Button 暗色
        Styles.Add(new Style(f => f.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.Parse("#0E639C"))),
                new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.Parse("#FFFFFF"))),
                new Setter(Button.BorderBrushProperty, new SolidColorBrush(Color.Parse("#0E639C"))),
            }
        });

        // CheckBox 暗色
        Styles.Add(new Style(f => f.OfType<CheckBox>())
        {
            Setters =
            {
                new Setter(CheckBox.ForegroundProperty, new SolidColorBrush(Color.Parse("#D4D4D4"))),
            }
        });
    }
}
