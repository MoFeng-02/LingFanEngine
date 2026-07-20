namespace LingFanEngine.SDK.Constants;

/// <summary>
/// 模板更新默认配置常量。
/// <para>manifest URL 默认指向仓库 main 分支的 updates/template-latest.json；可被 SdkSettings.TemplateUpdateManifestUrl 覆盖。</para>
/// </summary>
public static class TemplateDefaults
{
    /// <summary>默认模板 manifest 拉取地址（GitHub raw，避免 GitHub API 限流）。</summary>
    public const string DefaultManifestUrl =
        "https://raw.githubusercontent.com/MoFeng-02/LingFanEngine/main/updates/template-latest.json";

    /// <summary>SDK 内置模板版本（嵌入 template.zip 的版本基线；下载版本更高时覆盖）。</summary>
    public const string BuiltinVersion = "1.0.0";

    /// <summary>IHttpClientFactory 命名客户端名称（与引擎更新共用 engine-update 客户端，避免重复注册）。</summary>
    public const string HttpClientName = "engine-update";

    /// <summary>模板缓存目录名（位于 %LOCALAPPDATA%/LingFanEngine/ 下）。</summary>
    public const string TemplateCacheDir = "template-cache";

    /// <summary>模板缓存中当前生效模板的子目录名。</summary>
    public const string CurrentDirName = "current";

    /// <summary>模板缓存版本锁定文件名。</summary>
    public const string TemplateLockFileName = "template.lock.json";

    /// <summary>下载的模板 zip 缓存文件名。</summary>
    public const string TemplateZipFileName = "LingFanEngine.Template.zip";

    /// <summary>解压临时目录名（复用引擎更新工作目录）。</summary>
    public const string ExtractedDirName = "template-extracted";
}
