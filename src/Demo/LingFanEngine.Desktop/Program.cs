using System;
using Avalonia;
using LingFanEngine.Entry;

namespace LingFanEngine.Desktop;

public sealed class Setup
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static AppBuilder AppBuilder { get; set; } = null!;
    [STAThread]
    public static void Main(string[] args)
    {
        AppBuilder
        .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        return AppBuilder;
    }



}