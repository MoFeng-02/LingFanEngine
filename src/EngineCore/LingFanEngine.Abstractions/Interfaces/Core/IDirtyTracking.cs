namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 脏键追踪接口——用于检查点增量深拷贝优化。
/// <para>实现者（StateContainer）在写锁保护下跟踪所有被 Set 过的键，
/// 供 DslExecutor.CreateCheckpoint 仅深拷贝变更过的键，
/// 未变更键复用上一个检查点的深拷贝。</para>
/// <para>线程安全：GetSnapshotAndClearDirty 在写锁内原子执行，
/// 保证「状态快照」与「脏键集合」的一致性——不会出现值已写但脏键已清的竞态。</para>
/// </summary>
public interface IDirtyTracking
{
    /// <summary>
    /// 原子获取状态快照并清除脏键集合。
    /// <para>在写锁保护下执行：GetSnapshot + 读取脏键 + 清除脏键。
    /// 确保快照中的每个值与其脏键标记一致。</para>
    /// <para>调用方（DslExecutor.CreateCheckpoint）根据返回的脏键集合决定：
    /// 脏键 → 深拷贝当前值；非脏键 → 复用上一检查点的深拷贝。</para>
    /// </summary>
    /// <returns>(状态快照, 自上次调用以来被 Set 过的键集合)</returns>
    (IReadOnlyDictionary<string, object?> Snapshot, IReadOnlyCollection<string> DirtyKeys) GetSnapshotAndClearDirty();
}
