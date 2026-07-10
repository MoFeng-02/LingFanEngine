using _LingFanEngineTemplateTitle_.Desktop;
using Avalonia;

Setup.BuildAvaloniaApp()
    .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Vulkan, Win32RenderingMode.AngleEgl, Win32RenderingMode.Software] });

Setup.Main(args);