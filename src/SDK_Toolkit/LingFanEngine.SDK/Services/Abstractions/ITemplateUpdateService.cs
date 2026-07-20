using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>模板更新服务（从 GitHub Release 拉取模板 zip 并做版本管理）</summary>
public interface ITemplateUpdateService
{
    /// <summary>当前生效的模板版本（读模板缓存 lock，无则回退内置版本）。</summary>
    string CurrentTemplateVersion { get; }

    /// <summary>拉取模板 manifest 并比对版本，返回有更新的 manifest；已最新或无网络返回 null。</summary>
    Task<TemplateUpdateManifest?> CheckForTemplateUpdatesAsync(CancellationToken ct = default);

    /// <summary>下载模板 zip → sha256 校验 → 解压覆盖本地模板缓存 → 写 template.lock.json。</summary>
    Task<TemplateUpdateResult> UpdateTemplateAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 返回当前最佳模板源目录（下载缓存且版本高于内置时）；否则 null（调用方回退内置嵌入 zip）。
    /// </summary>
    string? GetCachedTemplateDir();
}
