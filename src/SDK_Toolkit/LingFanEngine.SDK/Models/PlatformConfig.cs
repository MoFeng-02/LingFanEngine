using System.Collections.Generic;

namespace LingFanEngine.SDK.Models;

/// <summary>目标平台配置</summary>
public class PlatformConfig
{
    /// <summary>平台名称（Windows/Linux/macOS/Android/iOS/Browser）</summary>
    public string Name { get; set; } = "";

    /// <summary>.NET Runtime Identifier（win-x64, linux-x64, osx-arm64 等）</summary>
    public string RuntimeIdentifier { get; set; } = "";

    /// <summary>是否支持 AOT</summary>
    public bool SupportsAot { get; set; }

    /// <summary>输出格式（exe/apk/dll/wasm）</summary>
    public string OutputFormat { get; set; } = "";

    /// <summary>预定义平台</summary>
    public static readonly PlatformConfig Windows = new()
    {
        Name = "Windows",
        RuntimeIdentifier = "win-x64",
        SupportsAot = true,
        OutputFormat = "exe"
    };

    public static readonly PlatformConfig Linux = new()
    {
        Name = "Linux",
        RuntimeIdentifier = "linux-x64",
        SupportsAot = true,
        OutputFormat = ""
    };

    public static readonly PlatformConfig MacOS = new()
    {
        Name = "macOS",
        RuntimeIdentifier = "osx-arm64",
        SupportsAot = true,
        OutputFormat = "app"
    };

    /// <summary>获取所有受支持的桌面平台</summary>
    public static IReadOnlyList<PlatformConfig> DesktopPlatforms => [Windows, Linux, MacOS];
}
