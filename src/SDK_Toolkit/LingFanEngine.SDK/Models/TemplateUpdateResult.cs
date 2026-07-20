namespace LingFanEngine.SDK.Models;

/// <summary>模板更新操作结果状态</summary>
public enum TemplateUpdateStatus
{
    /// <summary>已是最新，无需更新</summary>
    UpToDate,

    /// <summary>远端有更新可用（仅 CheckForTemplateUpdatesAsync 返回）</summary>
    UpdateAvailable,

    /// <summary>更新已应用（模板缓存已覆盖）</summary>
    UpdateApplied,

    /// <summary>更新失败（下载 / 校验 / 写入异常）</summary>
    Failed,
}

/// <summary>
/// 模板更新操作的返回结果（内存传递，不参与 JSON 持久化）。
/// </summary>
public class TemplateUpdateResult
{
    public TemplateUpdateStatus Status { get; set; }

    /// <summary>本次操作对应的远端清单版本（若涉及清单）。</summary>
    public string? ManifestVersion { get; set; }

    /// <summary>失败时的错误信息（Status == Failed 时填写）。</summary>
    public string? ErrorMessage { get; set; }

    public static TemplateUpdateResult UpToDate() => new() { Status = TemplateUpdateStatus.UpToDate };

    public static TemplateUpdateResult Failed(string message) => new()
    {
        Status = TemplateUpdateStatus.Failed,
        ErrorMessage = message,
    };
}
