namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 回溯检查点 — 记录某个交互时刻的完整状态快照（统一线性回溯时间线 Phase 16/16.1）
/// <para>DSL 执行到 say/menu/input/wait/scene_idle/navigate 时创建一个检查点。</para>
/// <para>回溯 = 恢复检查点的状态 + 重新执行该交互命令。</para>
/// <para>前进 = 恢复下一个检查点的状态 + 重新执行该交互命令。</para>
/// <para>检查点是会话级的（不写入存档），读档后从当前状态重新开始积累。</para>
/// </summary>
public class RollbackCheckpoint
{
    /// <summary>创建此检查点时的 DSL 命令索引（交互命令的索引）</summary>
    public int CommandIndex { get; set; }

    /// <summary>
    /// 创建此检查点时的场景名
    /// <para>用于跨场景回退：回退到不同场景的检查点时，自动重载该场景的命令列表。</para>
    /// </summary>
    public string SceneName { get; set; } = "";

    /// <summary>
    /// 交互类型："dialog" / "menu" / "input" / "wait"
    /// <para>用于回溯时正确恢复交互状态。</para>
    /// </summary>
    public string InteractionType { get; set; } = "dialog";

    /// <summary>
    /// 此时刻的全量状态快照（排除回溯自身的键）
    /// <para>包含用户变量 + 可视系统变量（背景、场景元素、运行时元素等）。</para>
    /// <para>不包含 __rollback_* 键（避免递归）。</para>
    /// </summary>
    public Dictionary<string, object?> StateSnapshot { get; set; } = new();
}
