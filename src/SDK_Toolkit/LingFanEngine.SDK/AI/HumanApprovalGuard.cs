using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.AI;

/// <summary>审批提示委托：给定整轮变更集与预览，返回是否批准 + 备注。</summary>
public delegate Task<(bool Approved, string Note)> ApprovalPromptAsync(
    IReadOnlyList<FileEdit> edits, string preview, CancellationToken ct);

/// <summary>
/// 人在环审批守卫——对整轮文件变更集（<see cref="FileEdit"/>）一次性放行/拒绝。
/// <para>模式：AutoApprove=true 自动批准（自动化/测试）；AutoApprove=false 且给定 <see cref="Prompt"/> 时询问；
/// 否则默认拒绝（须由 UI 接管）。</para>
/// </summary>
public sealed class HumanApprovalGuard
{
    /// <summary>自动批准（跳过人环确认）。</summary>
    public bool AutoApprove { get; init; }

    /// <summary>审批提示回调（AutoApprove=false 时使用）。</summary>
    public ApprovalPromptAsync? Prompt { get; init; }

    /// <summary>对一批编辑做审批。</summary>
    public async Task<bool> ApproveAsync(IReadOnlyList<FileEdit> edits, string preview, CancellationToken ct)
    {
        if (edits.Count == 0)
            return true; // 无变更无需审批

        if (AutoApprove)
            return true;

        if (Prompt != null)
        {
            var (ok, _) = await Prompt(edits, preview, ct).ConfigureAwait(false);
            return ok;
        }

        return false; // 无 UI 接管时默认拒绝，避免静默写盘
    }
}