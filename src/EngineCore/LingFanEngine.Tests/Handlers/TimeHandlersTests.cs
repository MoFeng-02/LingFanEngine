using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// TimeHandlers 集成测试：时间事件注册/注销/恢复、暂停/恢复/跳过时间。
/// </summary>
public class TimeHandlersTests
{
    [Fact]
    public void TimeEventHandler_RegistersTimeEventEntity()
    {
        var ctx = new FakeCommandContext { EventScheduler = new FakeEventScheduler() };
        new TimeEventHandler().Handle(new TimeEventCommand { TriggerDay = 3, Target = "scene_a" }, ctx);
        var sched = (FakeEventScheduler)ctx.EventScheduler!;
        sched.Entities.Should().ContainSingle(e => e.TargetPath == "scene_a" && e.TriggerDay == 3);
    }

    [Fact]
    public void SetTimeEventHandler_RegistersRegistration()
    {
        var ctx = new FakeCommandContext { EventScheduler = new FakeEventScheduler() };
        new SetTimeEventHandler().Handle(new SetTimeEventCommand { Id = "e1", Hour = 8, Minute = 30 }, ctx);
        var sched = (FakeEventScheduler)ctx.EventScheduler!;
        sched.Registrations.Should().ContainSingle(r => r.Id == "e1" && r.Hour == 8 && r.Minute == 30);
    }

    [Fact]
    public void UnregisterTimeEventHandler_RecordsUnregisterWithMode()
    {
        var ctx = new FakeCommandContext { EventScheduler = new FakeEventScheduler() };
        new UnregisterTimeEventHandler().Handle(
            new UnregisterTimeEventCommand { Id = "e1", Mode = UnregisterMode.Permanent }, ctx);
        var sched = (FakeEventScheduler)ctx.EventScheduler!;
        sched.Unregistered.Should().Contain("e1:Permanent");
        sched.Blocked.Should().Contain("e1");
    }

    [Fact]
    public void RestoreTimeEventHandler_ReRegistersFromRegistry()
    {
        var ctx = new FakeCommandContext
        {
            EventScheduler = new FakeEventScheduler(),
            TimeEventRegistry = new FakeTimeEventRegistry()
        };
        var reg = new TimeEventRegistration { Id = "e1", Hour = 9 };
        ((FakeTimeEventRegistry)ctx.TimeEventRegistry!).Seed("e1", reg);
        var before = ((FakeEventScheduler)ctx.EventScheduler!).Registrations.Count;
        new RestoreTimeEventHandler().Handle(new RestoreTimeEventCommand { Id = "e1" }, ctx);
        ((FakeEventScheduler)ctx.EventScheduler!).Registrations.Count.Should().Be(before + 1);
    }

    [Fact]
    public void TimePauseHandler_And_ResumeHandler_SetPausedState()
    {
        var ctx = new FakeCommandContext();
        new TimePauseHandler().Handle(new TimePauseCommand(), ctx);
        ctx.State.Get<bool>(StateKeys.GameTime.Paused).Should().BeTrue();
        new TimeResumeHandler().Handle(new TimeResumeCommand(), ctx);
        ctx.State.Get<bool>(StateKeys.GameTime.Paused).Should().BeFalse();
    }

    [Fact]
    public void SkipTimeHandler_CallsTimeServiceSkip()
    {
        var ctx = new FakeCommandContext { TimeService = new FakeGameTimeService() };
        new SkipTimeHandler().Handle(new SkipTimeCommand { Minutes = 120 }, ctx);
        ((FakeGameTimeService)ctx.TimeService!).SkipTimeMinutes.Should().Be(120);
    }
}
