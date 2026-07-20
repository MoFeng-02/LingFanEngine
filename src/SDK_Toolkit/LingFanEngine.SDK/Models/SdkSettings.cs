namespace LingFanEngine.SDK.Models;

/// <summary>
/// SDK 设置模型（P2-4）
/// <para>所有编辑器/构建/SDK 设置项，持久化到 AppData/sdk_settings.json。</para>
/// </summary>
public class SdkSettings
{
    // 编辑器设置
    public string EditorFontFamily { get; set; } = "Consolas";
    public int EditorFontSize { get; set; } = 14;
    public string IndentStyle { get; set; } = "spaces"; // "spaces" or "tabs"
    public int IndentWidth { get; set; } = 4;
    public bool FormatOnSave { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    public bool ShowMinimap { get; set; }
    public bool WordWrap { get; set; }

    // 主题
    public string Theme { get; set; } = "dark"; // "dark" or "light"

    // 构建
    public string DefaultBuildConfig { get; set; } = "Release";
    public bool DefaultSelfContained { get; set; } = true;
    public bool DefaultPublishAot { get; set; } = true;

    // SDK 信息（只读）
    public string SdkVersion { get; set; } = "0.1.0";
    public string EngineVersion { get; set; } = "1.0.0";
    public string DotNetVersion { get; set; } = "10.0";

    // 引擎更新
    /// <summary>引擎更新 manifest 拉取地址（GitHub Release 清单）。空则使用 EngineUpdateDefaults.DefaultManifestUrl。</summary>
    public string EngineUpdateManifestUrl { get; set; } = "";

    /// <summary>模板更新 manifest 拉取地址（GitHub Release 模板清单）。空则使用 TemplateDefaults.DefaultManifestUrl。</summary>
    public string TemplateUpdateManifestUrl { get; set; } = "";

    /// <summary>是否在启动时自动检查引擎更新（默认关闭，避免无谓联网）。</summary>
    public bool AutoCheckEngineUpdate { get; set; }
}
