namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 回溯检查点 — 记录某个 Say 时刻的完整状态快照
/// <para>每次 DSL 执行到 ShowDialogCommand 时创建一个检查点。</para>
/// <para>回溯 = 恢复上一个检查点的状态 + 重新展示该 Say。</para>
/// <para>前进 = 恢复下一个检查点的状态 + 重新展示该 Say。</para>
/// <para>检查点是会话级的（不写入存档），读档后从当前状态重新开始积累。</para>
/// </summary>
public class RollbackCheckpoint
{
    /// <summary>创建此检查点时的 DSL 命令索引（ShowDialogCommand 的索引）</summary>
    public int CommandIndex { get; set; }

    /// <summary>
    /// 此时刻的全量状态快照（排除回溯自身的键）
    /// <para>包含用户变量 + 可视系统变量（背景、场景元素、运行时元素等）。</para>
    /// <para>不包含 __rollback_* 键（避免递归）。</para>
    /// </summary>
    public Dictionary<string, object?> StateSnapshot { get; set; } = new();
}
