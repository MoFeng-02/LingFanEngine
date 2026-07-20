using System.Collections.Generic;

namespace LingFanEngine.SDK.Models;

/// <summary>
/// pending 更新条目——单个因被锁定而未应用的 DLL。
/// </summary>
public class PendingUpdateEntry
{
    /// <summary>DLL 文件名（如 LingFanEngine.Abstractions.dll）。</summary>
    public string DllName { get; set; } = string.Empty;

    /// <summary>目标覆盖路径（SDK 输出目录下的 DLL 完整路径）。</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>暂存的已校验 DLL 路径（updates/pending/ 下的完整路径）。</summary>
    public string StagedPath { get; set; } = string.Empty;

    /// <summary>该 DLL 的 sha256（应用后可再校验一次）。</summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// pending 更新清单——持久化到 updates/pending.json，SDK 启动时读取并尝试应用。
/// <para>用于处理 SDK 自带 DLL 缓存中被进程锁定的 DLL（Abstractions/DslCore/Pidgin）：
/// 下载校验后暂存到 pending 目录，下次启动时（JIT 加载前）尝试覆盖。</para>
/// </summary>
public class PendingUpdateManifest
{
    /// <summary>该 pending 对应的远端引擎版本。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>待应用的 DLL 条目列表。</summary>
    public List<PendingUpdateEntry> Entries { get; set; } = new();
}
