using System;
using Avalonia;

namespace _LingFanEngineTemplateTitle_.Desktop;

public sealed class Setup
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static AppBuilder AppBuilder { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args) => AppBuilder
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var app = AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();

        return app;
    }
}
