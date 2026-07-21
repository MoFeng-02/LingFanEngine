namespace LingFanEngine.SDK.Constants;

/// <summary>
/// 引擎 DLL 独立更新的默认配置常量。
/// <para>manifest URL 默认指向仓库 main 分支的 updates/latest.json；可被 SdkSettings.EngineUpdateManifestUrl 覆盖。</para>
/// </summary>
public static class EngineUpdateDefaults
{
    /// <summary>默认 manifest 拉取地址（GitHub raw，避免 GitHub API 限流与复杂解析）。</summary>
    public const string DefaultManifestUrl =
        "https://raw.githubusercontent.com/MoFeng-02/LingFanEngine/main/updates/latest.json";

    /// <summary>HTTP 请求超时（秒）。GitHub raw/asset 通常较快，给 60s 余量。</summary>
    public const int RequestTimeoutSeconds = 60;

    /// <summary>IHttpClientFactory 命名客户端名称（DI 注册与 CreateClient 共用）。</summary>
    public const string HttpClientName = "engine-update";

    /// <summary>下载的 zip 缓存文件名。</summary>
    public const string AssetZipFileName = "LingFanEngine.DLLs.zip";

    /// <summary>解压临时目录名。</summary>
    public const string ExtractedDirName = "extracted";

    /// <summary>pending 暂存目录名（存放因锁定未应用的 DLL）。</summary>
    public const string PendingDirName = "pending";

    /// <summary>pending 清单文件名。</summary>
    public const string PendingManifestFileName = "pending.json";

    /// <summary>HTTP User-Agent（GitHub API 强制要求；raw 也建议带）。</summary>
    public const string UserAgent = "LingFanEngine-SDK";

    /// <summary>
    /// 允许拉取 manifest 的主机白名单（67.6 安全）。
    /// <para>仅官方 GitHub 源可拉取——防止恶意 manifest 投毒（指向伪造 asset 的 zip）。
    /// 支持精确匹配与子域（如 *.githubusercontent.com、*.github.io）。</para>
    /// </summary>
    public static readonly string[] AllowedManifestHosts =
    [
        "raw.githubusercontent.com",
        "github.com",
        "github.io",
        "gitee.com",
    ];
}
