using System.Collections.Generic;
using LingFanEngine.SDK.Constants;

namespace LingFanEngine.SDK.Models;

/// <summary>
/// 引擎 DLL 更新清单（发布到 GitHub Release 的 manifest.json）。
/// <para>SDK 通过 IHttpClientFactory 拉取此清单，比对版本后决定是否下载 asset zip 更新引擎 DLL。</para>
/// <para>字段命名遵循 camelCase（由 SdkJsonContext 的 JsonSourceGenerationOptions 统一处理）。</para>
/// </summary>
public class EngineUpdateManifest
{
    /// <summary>远端引擎版本号（语义化版本 X.Y.Z），作为全局兜底版本。</summary>
    /// <remarks>当 <see cref="DllVersions"/> 未提供时，所有 4 个 DLL 都用此版本比对；
    /// 若提供了 <see cref="DllVersions"/>，则按各 DLL 自身的版本比对，从而支持「只更新某个依赖」的情形。</remarks>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 逐 DLL 的版本号（可选，key=DLL 文件名，value=语义化版本 X.Y.Z）。
    /// <para>用于细粒度版本隔离——例如某次发布只修了 Pidgin 解析器，仅 Pidgin 版本号 +1，
    /// 其余 DLL 版本不变。提供后，<see cref="GetDllVersion"/> 会优先返回对应 DLL 的版本，
    /// 避免「LingFanEngine.dll 版本没变就误判已最新」而漏掉其它 DLL 的更新。</para>
    /// </summary>
    public Dictionary<string, string> DllVersions { get; set; } = new();

    /// <summary>所需的最低 SDK 版本（语义化版本）。本地 SDK 版本低于此值时拒绝热更，提示升级 SDK。</summary>
    public string SdkVersion { get; set; } = string.Empty;

    /// <summary>Release 说明页 URL（GitHub Release 页面），供 UI 展示"查看更新内容"。</summary>
    public string ReleaseNotesUrl { get; set; } = string.Empty;

    /// <summary>DLLs.zip 资产的下载地址（GitHub Release asset 的 browser_download_url，会 302 跳转）。</summary>
    public string AssetUrl { get; set; } = string.Empty;

    /// <summary>整包 zip 的 sha256（可选，整包校验，与逐 DLL 校验互为补充）。</summary>
    public string AssetSha256 { get; set; } = string.Empty;

    /// <summary>
    /// 是否包含破坏性的 Abstractions 接口变更。
    /// <para>true 时不可热更——Abstractions 是 SDK 自身依赖的契约层，变更须升级 SDK 整体而非替换 DLL。</para>
    /// </summary>
    public bool BreakingAbstractions { get; set; }

    /// <summary>
    /// 逐 DLL 破坏性标记（v2，可选）：列出本次包含破坏性契约变更的 DLL 名。
    /// <para>这些 DLL 不可热更，需升级 SDK 整体；与之相对，未列入的 DLL 仍可正常热替换。
    /// 用于更精细的兼容性控制（如仅 Abstractions 破坏性，但 DslCore/Pidgin 可热更）。</para>
    /// </summary>
    public List<string> BreakingDlls { get; set; } = new();

    /// <summary>发布时间（UTC，ISO8601，v2 可选）。用于 UI 展示"发布于何时"。</summary>
    public string PublishedUtc { get; set; } = string.Empty;

    /// <summary>
    /// 所需最低 SDK 版本（v2，可选，语义化版本 X.Y.Z）。
    /// <para>本地 SDK 版本低于此值时拒绝热更（<see cref="EngineUpdateService"/> 校验），提示用户先升级 SDK。</para>
    /// </summary>
    public string MinSdkVersion { get; set; } = string.Empty;

    /// <summary>逐 DLL 的 sha256 校验表（key=DLL 文件名，value=sha256 小写十六进制）。</summary>
    public Dictionary<string, string> DllChecksums { get; set; } = new();

    /// <summary>
    /// 取指定 DLL 的版本号：优先用 <see cref="DllVersions"/> 中声明的细粒度版本，
    /// 缺失时回落到全局 <see cref="Version"/>。
    /// </summary>
    public string GetDllVersion(string dllName)
    {
        if (DllVersions.TryGetValue(dllName, out var v) && !string.IsNullOrWhiteSpace(v))
            return v;
        return Version;
    }

    /// <summary>
    /// 返回清单中全部 4 个引擎 DLL 的版本表（key=DLL 文件名，value=语义化版本）。
    /// <para>供回写 engine.lock.json 的 DllVersions 使用。</para>
    /// </summary>
    public Dictionary<string, string> GetDllVersions()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in ProjectConstants.SdkDistributedDlls)
            map[dll] = GetDllVersion(dll);
        return map;
    }

    /// <summary>
    /// 指定 DLL 是否包含破坏性契约变更（出现在 <see cref="BreakingDlls"/> 中）。
    /// <para>破坏性 DLL 不可热更，需升级 SDK 整体；非破坏性 DLL 即使同批次发布也可正常热替换。</para>
    /// </summary>
    public bool IsDllBreaking(string dllName) =>
        BreakingDlls.Any(d => string.Equals(d, dllName, StringComparison.OrdinalIgnoreCase));
}
