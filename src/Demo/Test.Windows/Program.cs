using Avalonia;
using LingFanEngine.Desktop;

Setup.BuildAvaloniaApp()
    .With(new Win32PlatformOptions
    {
        // GpuMediaPlayer 已退役（改用 WebView 承载视频），Vulkan 重新置顶为桌面首选渲染后端。
        RenderingMode = [Win32RenderingMode.Vulkan, Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software]
    });

Setup.Main(args);