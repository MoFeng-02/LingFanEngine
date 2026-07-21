using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Events;
using Xunit;

namespace LingFanEngine.Tests.Events;

/// <summary>
/// S2（时间 RestoreEvent 矩阵）测试：按"快照即真相"模型锁死事件生命周期。
/// <para>原则（用户拍板 2026-07-21）：存档快照（时间锚点）是事件生命周期的单一真相源。</para>
/// <para>restore_time_event 只能复活 Suspended（快照标记为可复活）；FiredOneShot/Destroyed 是终态，
/// 快照里有就不能复活；三个集合都没有就是活态，restore 幂等无副作用。</para>
/// <para>本组测试确保未来改动 RestoreEvent/RegisterEvent 时不会误清终态集合导致"已发生的事被复活"。</para>
/// </summary>
public class EventSchedulerRestoreMatrixTests
{
    private static EventScheduler CreateScheduler(out FakeGameTimeService time)
    {
        time = new FakeGameTimeService();
        return new EventScheduler(time, new StateContainer());
    }

    private static TimeEventRegistration MakeReg(string id, int hour, int? minute = null, bool oneShot = false)
        => new()
        {
            Id = id,
            Hour = hour,
            Minute = minute,
            IsOneShot = oneShot,
            IsLegacyNavigation = false
        };

    // ========== 运行时（内存态）矩阵 ==========

    [Fact]
    public void RestoreEvent_Suspended_RestoresAndAllowsReregister()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 12));

        scheduler.UnregisterEvent("e1", UnregisterMode.Temporary).Should().BeTrue();
        scheduler.IsBlocked("e1").Should().BeTrue();

        // Suspended → restore 成功，复活
        scheduler.RestoreEvent("e1").Should().BeTrue();
        scheduler.IsBlocked("e1").Should().BeFalse();
        scheduler.RegisterEvent(MakeReg("e1", 12)).Should().BeTrue();
    }

    [Fact]
    public void RestoreEvent_FiredOneShot_RemainsBlocked_CannotRevive()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("one", 8, oneShot: true));

        // 运行时触发 → 进入 FiredOneShot（终态）
        scheduler.MarkFired("one");
        scheduler.IsBlocked("one").Should().BeTrue();

        // 快照有（FiredOneShot）→ restore 不能复活
        scheduler.RestoreEvent("one").Should().BeFalse();
        scheduler.IsBlocked("one").Should().BeTrue();
        scheduler.RegisterEvent(MakeReg("one", 8, oneShot: true)).Should().BeFalse();
    }

    [Fact]
    public void RestoreEvent_Destroyed_RemainsBlocked_CannotRevive()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 12));

        // 永久销毁（终态）
        scheduler.UnregisterEvent("e1", UnregisterMode.Permanent).Should().BeTrue();
        scheduler.IsBlocked("e1").Should().BeTrue();

        // 终态 → restore 不能复活
        scheduler.RestoreEvent("e1").Should().BeFalse();
        scheduler.IsBlocked("e1").Should().BeTrue();
        scheduler.RegisterEvent(MakeReg("e1", 12)).Should().BeFalse();
    }

    [Fact]
    public void RestoreEvent_LiveEvent_NoOp_Idempotent()
    {
        var scheduler = CreateScheduler(out _);
        // 活态：三集合都没有
        scheduler.RegisterEvent(MakeReg("live", 12));

        // 对活态事件 restore → 幂等 no-op（不是 Suspended，TryRemove 落空）
        scheduler.RestoreEvent("live").Should().BeFalse();
        scheduler.IsBlocked("live").Should().BeFalse();
        scheduler.EventCount.Should().Be(1); // 仍在活跃集合，未被误删
        // 重复注册被去重层挡掉（幂等）
        scheduler.RegisterEvent(MakeReg("live", 12)).Should().BeFalse();
    }

    // ========== 快照（存档锚点）矩阵 ==========

    [Fact]
    public void SnapshotRestore_RestoreOnlyClearsSuspended_TerminalStatesStayBlocked()
    {
        // 模拟存档快照：三类 ID 都在快照里
        var state = new TimeEventSaveState
        {
            FiredOneShotIds = new HashSet<string> { "fired" },
            DestroyedIds = new HashSet<string> { "dead" },
            SuspendedIds = new HashSet<string> { "susp" }
        };

        var scheduler = CreateScheduler(out _);
        scheduler.ApplySaveState(state);

        // 快照即真相：三类都被还原为 blocked
        scheduler.IsBlocked("fired").Should().BeTrue();
        scheduler.IsBlocked("dead").Should().BeTrue();
        scheduler.IsBlocked("susp").Should().BeTrue();

        // restore 只清 Suspended
        scheduler.RestoreEvent("susp").Should().BeTrue();
        scheduler.IsBlocked("susp").Should().BeFalse();

        // 终态不可经 restore 复活
        scheduler.RestoreEvent("fired").Should().BeFalse();
        scheduler.RestoreEvent("dead").Should().BeFalse();
        scheduler.IsBlocked("fired").Should().BeTrue();
        scheduler.IsBlocked("dead").Should().BeTrue();
    }

    [Fact]
    public void SnapshotRestore_FiredOneShot_NotRevivedByRestore_RejectsReregister()
    {
        // 直接从存档快照恢复一个已触发的单次事件（最常见的"读档后不应再触发"场景）
        var state = new TimeEventSaveState
        {
            FiredOneShotIds = new HashSet<string> { "fired" }
        };

        var scheduler = CreateScheduler(out _);
        scheduler.ApplySaveState(state);

        scheduler.IsBlocked("fired").Should().BeTrue();
        scheduler.RestoreEvent("fired").Should().BeFalse(); // 不能复活
        scheduler.RegisterEvent(MakeReg("fired", 8, oneShot: true)).Should().BeFalse();
    }
}
