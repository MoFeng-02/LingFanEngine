using _LingFanEngineTemplateTitle_.Desktop;
using Avalonia;

Setup.BuildAvaloniaApp()
   .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Vulkan, X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software] });

Setup.Main(args);