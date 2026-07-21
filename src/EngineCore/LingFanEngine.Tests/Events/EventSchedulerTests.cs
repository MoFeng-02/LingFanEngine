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
/// EventScheduler 测试：用 FakeGameTimeService 驱动时间匹配，覆盖注册/注销/存档/查询/出队等纯逻辑。
/// </summary>
public class EventSchedulerTests
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

    // ========== 注册 ==========

    [Fact]
    public void RegisterEvent_NewId_ReturnsTrueAndIncrementsCount()
    {
        var scheduler = CreateScheduler(out _);

        var ok = scheduler.RegisterEvent(MakeReg("e1", 12));

        ok.Should().BeTrue();
        scheduler.EventCount.Should().Be(1);
    }

    [Fact]
    public void RegisterEvent_DuplicateId_ReturnsFalse()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 12));

        var ok = scheduler.RegisterEvent(MakeReg("e1", 13));

        ok.Should().BeFalse();
        scheduler.EventCount.Should().Be(1);
    }

    // ========== 注销三模式 ==========

    [Fact]
    public void UnregisterEvent_Normal_RemovesEvent()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 12));

        var ok = scheduler.UnregisterEvent("e1", UnregisterMode.Normal);

        ok.Should().BeTrue();
        scheduler.EventCount.Should().Be(0);
    }

    [Fact]
    public void UnregisterEvent_Permanent_BlocksFutureRegister()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 12));

        scheduler.UnregisterEvent("e1", UnregisterMode.Permanent).Should().BeTrue();
        scheduler.IsBlocked("e1").Should().BeTrue();

        // 永久销毁后再次注册应被拒绝
        scheduler.RegisterEvent(MakeReg("e1", 12)).Should().BeFalse();
    }

    [Fact]
    public void UnregisterEvent_Temporary_BlocksUntilRestored()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 12));

        scheduler.UnregisterEvent("e1", UnregisterMode.Temporary).Should().BeTrue();
        scheduler.RegisterEvent(MakeReg("e1", 12)).Should().BeFalse();

        scheduler.RestoreEvent("e1").Should().BeTrue();
        scheduler.RegisterEvent(MakeReg("e1", 12)).Should().BeTrue();
    }

    [Fact]
    public void IsBlocked_ReflectsSuspendedAndFiredOneShot()
    {
        // 触发匹配使单次事件进入 fired 集合
        var time = new FakeGameTimeService { CurrentHour = 8, CurrentMinute = 0 };
        var scheduler = new EventScheduler(time, new StateContainer());
        scheduler.RegisterEvent(MakeReg("one", 8, oneShot: true));
        time.RaiseTimeAdvanced();
        scheduler.TryDequeuePendingEvent(out _);
        scheduler.MarkFired("one");
        scheduler.IsBlocked("one").Should().BeTrue();
    }

    // ========== 查询 ==========

    [Fact]
    public void GetRegisteredEvents_OnlyReturnsLegacyNavigationEvents()
    {
        var scheduler = CreateScheduler(out _);
        // 回调驱动（非 legacy）
        scheduler.RegisterEvent(MakeReg("cb", 9));
        // legacy 导航驱动
        scheduler.RegisterEvent(new TimeEventEntity { TargetPath = "town", TriggerHour = 10 });

        var legacy = scheduler.GetRegisteredEvents();
        legacy.Should().HaveCount(1);
        legacy[0].TargetPath.Should().Be("town");
        legacy[0].TriggerHour.Should().Be(10);
    }

    [Fact]
    public void RegisterEvents_BulkRegistersLegacyEvents()
    {
        var scheduler = CreateScheduler(out _);
        var events = new List<TimeEventEntity>
        {
            new() { TargetPath = "a", TriggerHour = 1 },
            new() { TargetPath = "b", TriggerHour = 2 }
        };

        scheduler.RegisterEvents(events);

        scheduler.GetRegisteredEvents().Should().HaveCount(2);
    }

    // ========== 时间匹配 ==========

    [Fact]
    public void TimeAdvanced_MatchingHourMinute_EnqueuesPending()
    {
        var scheduler = CreateScheduler(out var time);
        scheduler.RegisterEvent(MakeReg("e1", 12, minute: 30));

        time.CurrentHour = 12;
        time.CurrentMinute = 30;
        time.RaiseTimeAdvanced();

        scheduler.TryDequeuePendingEvent(out var evt).Should().BeTrue();
        evt!.Id.Should().Be("e1");
    }

    [Fact]
    public void TimeAdvanced_NonMatchingMinute_DoesNotEnqueue()
    {
        var scheduler = CreateScheduler(out var time);
        scheduler.RegisterEvent(MakeReg("e1", 12, minute: 30));

        time.CurrentHour = 12;
        time.CurrentMinute = 45; // 不匹配
        time.RaiseTimeAdvanced();

        scheduler.TryDequeuePendingEvent(out _).Should().BeFalse();
    }

    [Fact]
    public void TimeAdvanced_MinuteNull_MatchesOnTheHour()
    {
        var scheduler = CreateScheduler(out var time);
        scheduler.RegisterEvent(MakeReg("e1", 12, minute: null));

        time.CurrentHour = 12;
        time.CurrentMinute = 0;
        time.RaiseTimeAdvanced();

        scheduler.TryDequeuePendingEvent(out var evt).Should().BeTrue();
        evt!.Id.Should().Be("e1");
    }

    [Fact]
    public void TimeAdvanced_DayOfWeekFilter()
    {
        var scheduler = CreateScheduler(out var time);
        scheduler.RegisterEvent(new TimeEventRegistration
        {
            Id = "e1",
            Hour = 9,
            Minute = 0,
            DaysOfWeek = [DayOfWeek.Monday],
            IsLegacyNavigation = false
        });

        // 非周一：不触发
        time.CurrentHour = 9;
        time.CurrentMinute = 0;
        time.DayOfWeek = DayOfWeek.Tuesday;
        time.RaiseTimeAdvanced();
        scheduler.TryDequeuePendingEvent(out _).Should().BeFalse();

        // 周一：触发
        time.DayOfWeek = DayOfWeek.Monday;
        time.RaiseTimeAdvanced();
        scheduler.TryDequeuePendingEvent(out var evt).Should().BeTrue();
        evt!.Id.Should().Be("e1");
    }

    // ========== 单次触发 ==========

    [Fact]
    public void MarkFired_OneShot_RemovesAndBlocksReregister()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("one", 8, oneShot: true));

        scheduler.MarkFired("one");

        scheduler.EventCount.Should().Be(0);
        scheduler.RegisterEvent(MakeReg("one", 8, oneShot: true)).Should().BeFalse();
    }

    // ========== 存档 ==========

    [Fact]
    public void ApplySaveState_RestoresDestroyedAndClearsEvents()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("active", 1));

        var state = new TimeEventSaveState
        {
            DestroyedIds = new HashSet<string> { "dead" },
            SuspendedIds = new HashSet<string> { "susp" },
            FiredOneShotIds = new HashSet<string> { "fired" }
        };

        scheduler.ApplySaveState(state);

        // 事件被清空
        scheduler.EventCount.Should().Be(0);
        // 永久/暂时销毁标记被恢复
        scheduler.IsBlocked("dead").Should().BeTrue();
        scheduler.IsBlocked("susp").Should().BeTrue();
        scheduler.IsBlocked("fired").Should().BeTrue();
    }

    [Fact]
    public void ApplySaveState_Null_ClearsAll()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 1));
        scheduler.UnregisterEvent("e1", UnregisterMode.Permanent);

        scheduler.ApplySaveState(null);

        scheduler.EventCount.Should().Be(0);
        scheduler.IsBlocked("e1").Should().BeFalse(); // 即使之前 permanent，null 也清空
    }

    [Fact]
    public void GetSaveState_CapturesBlockedIds()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 1));
        scheduler.UnregisterEvent("e1", UnregisterMode.Permanent);

        var state = scheduler.GetSaveState();

        state.DestroyedIds.Should().Contain("e1");
    }

    // ========== 清理 ==========

    [Fact]
    public void ClearEvents_EmptiesAllCollections()
    {
        var scheduler = CreateScheduler(out _);
        scheduler.RegisterEvent(MakeReg("e1", 1));
        scheduler.UnregisterEvent("e1", UnregisterMode.Temporary);

        scheduler.ClearEvents();

        scheduler.EventCount.Should().Be(0);
        scheduler.IsBlocked("e1").Should().BeFalse();
    }
}
