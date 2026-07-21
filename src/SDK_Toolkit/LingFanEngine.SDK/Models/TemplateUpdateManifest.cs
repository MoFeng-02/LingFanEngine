namespace LingFanEngine.SDK.Models;

/// <summary>
/// 模板更新清单（对应 updates/template-latest.json）。
/// <para>SDK 通过 IHttpClientFactory 拉取此清单，比对版本后决定是否下载模板 zip 覆盖本地模板缓存。</para>
/// </summary>
public class TemplateUpdateManifest
{
    /// <summary>模板版本（X.Y.Z）。作为全局版本参与比对。</summary>
    public string Version { get; set; } = "0.0.0";

    /// <summary>模板 zip 资产下载地址（GitHub Release asset 的 browser_download_url）。</summary>
    public string AssetUrl { get; set; } = "";

    /// <summary>整包 zip 的 sha256（必需，校验防篡改）。</summary>
    public string AssetSha256 { get; set; } = "";

    /// <summary>发布时间（UTC ISO8601，可选）。</summary>
    public string PublishedUtc { get; set; } = "";

    /// <summary>最低 SDK 版本要求；本地 SDK 版本低于此值则拒绝热更模板（提示升级 SDK）。</summary>
    public string MinSdkVersion { get; set; } = "";

    /// <summary>更新说明 / Release 链接（可选）。</summary>
    public string ReleaseNotesUrl { get; set; } = "";

    /// <summary>
    /// 备用下载镜像（可选）。当 <see cref="AssetUrl"/> 下载失败时，SDK 会依次尝试此处列出的 URL。
    /// <para>典型用途：GitHub Release 不可达时自动 fallback 到 Gitee Release。</para>
    /// </summary>
    public List<string> Mirrors { get; set; } = new();
}
