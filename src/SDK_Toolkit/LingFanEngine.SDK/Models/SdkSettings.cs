using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Models;

/// <summary>
/// SDK 设置模型（P2-4）
/// <para>所有编辑器/构建/SDK 设置项，持久化到 AppData/sdk_settings.json。</para>
/// </summary>
public class SdkSettings
{
    // 界面
    /// <summary>SDK 界面语言（BCP 47，如 zh-Hans / en-US / ja-JP；空=默认中文）。</summary>
    public string UILanguage { get; set; } = "zh-Hans";

    // 构建
    public string DefaultBuildConfig { get; set; } = "Release";
    public bool DefaultSelfContained { get; set; } = true;
    public bool DefaultPublishAot { get; set; } = true;

    // 翻译
    /// <summary>新建项目时的默认翻译输出布局（全局用户默认；项目级 ProjectConfig.TranslationLayout 优先）。</summary>
    public TranslationLayout DefaultTranslationLayout { get; set; } = TranslationLayout.Flat;

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
