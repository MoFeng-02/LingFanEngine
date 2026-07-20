using System.Collections.Generic;

namespace LingFanEngine.SDK.Models;

/// <summary>
/// 引擎版本锁定文件（engine.lock.json）——声明「版本真相」。
/// <para>存在于两处：</para>
/// <list type="bullet">
/// <item>每项目根目录：声明该项目使用的引擎版本集（版本隔离的核心）。</item>
/// <item>引擎缓存目录（%LOCALAPPDATA%/LingFanEngine/engine-cache）：声明 SDK 已知最新引擎版本集，作为离线建项目/预览的种子。</item>
/// </list>
/// <para>AOT 安全：注册进 SdkJsonContext，源生成序列化。</para>
/// </summary>
public class EngineLockFile
{
    /// <summary>引擎整体版本（取核心 LingFanEngine.dll 的版本，便于人类阅读）。</summary>
    public string EngineVersion { get; set; } = "0.0.0";

    /// <summary>逐 DLL 版本表（key=DLL 文件名，value=X.Y.Z）。用于逐 DLL 版本隔离。</summary>
    public Dictionary<string, string> DllVersions { get; set; } = new();

    /// <summary>是否锁定当前版本。锁定后跳过自动检查与立即更新（用户主动固定）。</summary>
    public bool Pinned { get; set; }

    /// <summary>最近一次检查/更新的 UTC 时间（ISO 8601），用于可复现与审计。</summary>
    public string LastCheckedUtc { get; set; } = "";
}
