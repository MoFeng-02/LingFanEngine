using FluentAssertions;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Events;
using Xunit;

namespace LingFanEngine.Tests.Events;

/// <summary>
/// E4（时间锚点校验）测试：单次事件（绝对天锚点）早于当前游戏天应被识别为"永不触发"。
/// <para>世界模式时间不可逆——锚点天 &lt; 当前天的一次性事件永远匹配不上（currentDay != day），属静默丢失。
/// EventScheduler.RegisterEvent 已加 Debug.WriteLine 告警（不阻塞、不抛异常），本组锁死该行为。</para>
/// </summary>
public class EventSchedulerAnchorTests
{
    private static EventScheduler CreateScheduler(out FakeGameTimeService time)
    {
        time = new FakeGameTimeService();
        return new EventScheduler(time, new StateContainer());
    }

    private static TimeEventRegistration OneShotOnDay(string id, int day, int hour, int minute)
        => new()
        {
            Id = id,
            Hour = hour,
            Minute = minute,
            Day = day,
            IsOneShot = true,
            IsLegacyNavigation = false
        };

    [Fact]
    public void OneShot_AbsoluteDayMatchesCurrentDay_Fires()
    {
        var time = new FakeGameTimeService { CurrentDay = 5, CurrentHour = 12, CurrentMinute = 0 };
        var scheduler = new EventScheduler(time, new StateContainer());
        scheduler.RegisterEvent(OneShotOnDay("todayEvt", 5, 12, 0));

        time.RaiseTimeAdvanced();

        scheduler.TryDequeuePendingEvent(out var evt).Should().BeTrue();
        evt!.Id.Should().Be("todayEvt");
    }

    [Fact]
    public void OneShot_AbsoluteDayBeforeCurrent_NeverFires_NoException()
    {
        // 当前第 5 天，事件锚点在第 3 天（已过去）→ 永不触发
        var time = new FakeGameTimeService { CurrentDay = 5, CurrentHour = 12, CurrentMinute = 0 };
        var scheduler = new EventScheduler(time, new StateContainer());

        // 注册不抛异常（告警仅 Debug.WriteLine）
        var act = () => scheduler.RegisterEvent(OneShotOnDay("pastEvt", 3, 12, 0));
        act.Should().NotThrow();

        // 在匹配时刻推进时间 → 因 currentDay(5) != day(3) 永远跳过
        time.RaiseTimeAdvanced();
        scheduler.TryDequeuePendingEvent(out _).Should().BeFalse();

        // 之后每一天同一时刻仍不会触发（确认静默丢失，非偶发）
        time.CurrentDay = 6;
        time.RaiseTimeAdvanced();
        scheduler.TryDequeuePendingEvent(out _).Should().BeFalse();
        time.CurrentDay = 100;
        time.RaiseTimeAdvanced();
        scheduler.TryDequeuePendingEvent(out _).Should().BeFalse();
    }

    [Fact]
    public void Recurring_IntervalDayIgnoresPastAnchor_FiresOnNextMultiple()
    {
        // recurring 事件的 Day 是间隔（非绝对天），即使注册"晚"也会在下一个倍数触发——不应被 E4 告警阻断
        var time = new FakeGameTimeService { CurrentDay = 5, CurrentHour = 9, CurrentMinute = 0, TotalMinutes = 5 * 1440 };
        var scheduler = new EventScheduler(time, new StateContainer());
        scheduler.RegisterEvent(new TimeEventRegistration
        {
            Id = "recur",
            Hour = 9,
            Minute = 0,
            Day = 3, // 每 3 天
            IsOneShot = false,
            IsLegacyNavigation = false
        });

        // 第 5 天不是 3 的倍数 → 不触发
        time.RaiseTimeAdvanced();
        scheduler.TryDequeuePendingEvent(out _).Should().BeFalse();

        // 第 6 天（3 的倍数）→ 触发
        time.CurrentDay = 6;
        time.TotalMinutes = 6 * 1440;
        time.RaiseTimeAdvanced();
        scheduler.TryDequeuePendingEvent(out var evt).Should().BeTrue();
        evt!.Id.Should().Be("recur");
    }
}
