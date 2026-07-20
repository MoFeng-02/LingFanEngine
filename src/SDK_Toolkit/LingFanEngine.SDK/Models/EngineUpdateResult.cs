using System.Collections.Generic;

namespace LingFanEngine.SDK.Models;

/// <summary>引擎更新操作结果状态</summary>
public enum EngineUpdateStatus
{
    /// <summary>已是最新，无需更新</summary>
    UpToDate,

    /// <summary>远端有更新可用（仅 CheckForUpdatesAsync 返回）</summary>
    UpdateAvailable,

    /// <summary>更新已全部应用（热替换完成）</summary>
    UpdateApplied,

    /// <summary>部分 DLL 因被 SDK 进程锁定，已写入 pending，需重启 SDK 后应用</summary>
    PendingRestart,

    /// <summary>更新失败（下载/校验/写入异常）</summary>
    Failed,
}

/// <summary>
/// 引擎更新操作的返回结果。
/// <para>仅在内存中传递，不参与 JSON 持久化（pending 持久化使用 <see cref="PendingUpdateManifest"/>）。</para>
/// </summary>
public class EngineUpdateResult
{
    public EngineUpdateStatus Status { get; set; }

    /// <summary>本次操作对应的远端清单版本（若涉及清单）。</summary>
    public string? ManifestVersion { get; set; }

    /// <summary>已成功热替换的 DLL 文件名列表。</summary>
    public List<string> UpdatedDlls { get; set; } = new();

    /// <summary>已写入 pending、等待重启应用的 DLL 文件名列表。</summary>
    public List<string> PendingDlls { get; set; } = new();

    /// <summary>失败时的错误信息（Status == Failed 时填写）。</summary>
    public string? ErrorMessage { get; set; }

    public static EngineUpdateResult UpToDate() => new() { Status = EngineUpdateStatus.UpToDate };

    public static EngineUpdateResult Failed(string message) => new()
    {
        Status = EngineUpdateStatus.Failed,
        ErrorMessage = message,
    };
}
