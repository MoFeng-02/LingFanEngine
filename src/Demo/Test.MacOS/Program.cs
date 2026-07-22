using Avalonia;
using LingFanEngine.Desktop;

Setup.BuildAvaloniaApp()
    .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software] });

Setup.Main(args);