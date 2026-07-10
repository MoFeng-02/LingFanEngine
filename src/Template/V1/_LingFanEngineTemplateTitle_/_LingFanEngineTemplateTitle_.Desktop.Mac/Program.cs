using _LingFanEngineTemplateTitle_.Desktop;
using Avalonia;

Setup.BuildAvaloniaApp()
    .With(new AvaloniaNativePlatformOptions { RenderingMode = [AvaloniaNativeRenderingMode.Metal, AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software] });

Setup.Main(args);