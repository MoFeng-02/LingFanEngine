namespace LingFanEngine.SDK.Constants;

/// <summary>
/// 模板更新默认配置常量。
/// <para>manifest URL 默认指向 GitHub Release 的「latest」asset（releases/latest/download），
/// 始终解析到最新一次发布随附的 template-latest.json —— 由 CI 生成、带真实 sha256 与正确版本，
/// 无需 commit 回 main，也无需机器人 token。可被 SdkSettings.TemplateUpdateManifestUrl 覆盖。</para>
/// </summary>
public static class TemplateDefaults
{
    /// <summary>
    /// 默认模板 manifest 拉取地址（GitHub Release「latest」asset）。
    /// <para>用 releases/latest/download 而非 main 分支 raw：CI 只把 manifest 传成 Release asset，
    /// 不 commit 回 main，故必须从 Release 侧读取才能拿到 CI 算出的真实版本/sha256。</para>
    /// </summary>
    public const string DefaultManifestUrl =
        "https://github.com/MoFeng-02/LingFanEngine/releases/latest/download/template-latest.json";

    /// <summary>
    /// SDK 内置模板版本（嵌入 template.zip 的版本基线；下载版本更高时才覆盖）。
    /// <para>⚠ 必须与 src/Template/template-meta.json 的 version 保持一致：模板内容变更时两处同时 bump，
    /// 否则新构建的 SDK 会把已嵌入的新模板误判为旧版而重复下载。</para>
    /// </summary>
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
