using FluentAssertions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// GameTimeService 游戏时间服务测试
/// <para>用 StateContainer + LingFanEngineOptions 验证 tick/advance/day/hour/minute/pause/reset/event/day-of-week。</para>
/// </summary>
public class GameTimeServiceTests
{
    private static GameTimeService CreateEnabled(StateContainer state, int hour = 0, int minute = 0, int day = 1)
    {
        var options = new LingFanEngineOptions
        {
            EnableTimeSystem = true,
            TimeStartDay = day,
            TimeStartHour = hour,
            TimeStartMinute = minute
        };
        return new GameTimeService(state, options);
    }

    [Fact]
    public void Constructor_WhenEnabled_InitializesStartValues()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state, hour: 8, minute: 30);

        // 默认暂停
        service.IsPaused.Should().BeTrue();
        service.TotalMinutes.Should().Be(8 * 60 + 30);
        service.CurrentDay.Should().Be(1);
        service.CurrentHour.Should().Be(8);
        service.CurrentMinute.Should().Be(30);
        service.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Tick_WhenResumed_AdvancesOneMinute()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state, hour: 8, minute: 30);
        service.Resume();

        service.Tick();

        service.TotalMinutes.Should().Be(8 * 60 + 31);
        service.CurrentHour.Should().Be(8);
        service.CurrentMinute.Should().Be(31);
    }

    [Fact]
    public void Tick_WhenPaused_DoesNotAdvance()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state, hour: 8, minute: 30);
        // 默认暂停

        service.Tick();

        service.TotalMinutes.Should().Be(8 * 60 + 30);
    }

    [Fact]
    public void Pause_StopsAdvancement()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state);
        service.Resume();
        service.Tick();

        service.Pause();
        service.Tick();
        service.Tick();

        // 只前进了 1 分钟
        service.TotalMinutes.Should().Be(1);
    }

    [Fact]
    public void SkipTime_AdvancesByMinutes()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state);
        service.Resume();

        service.SkipTime(10);

        service.TotalMinutes.Should().Be(10);
        service.CurrentMinute.Should().Be(10);
    }

    [Fact]
    public void SkipTime_ZeroOrNegative_DoesNothing()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state);
        service.Resume();

        service.SkipTime(0);
        service.SkipTime(-5);

        service.TotalMinutes.Should().Be(0);
    }

    [Fact]
    public void CurrentDay_RollsOverAfter1440Minutes()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state);
        service.Resume();

        service.SkipTime(1440); // 恰好 1 天

        service.CurrentDay.Should().Be(2);
        service.DayOfWeek.Should().Be(DayOfWeek.Tuesday);
    }

    [Fact]
    public void Reset_RestoresInitialState()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state, hour: 9, minute: 0);
        service.Resume();
        service.SkipTime(500);

        service.Reset();

        service.TotalMinutes.Should().Be(9 * 60);
        service.IsPaused.Should().BeTrue();
        service.TimeScale.Should().Be(1.0f);
    }

    [Fact]
    public void OnTimeAdvanced_FiresWithCorrectArgs()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state, hour: 8, minute: 30);
        service.Resume();

        GameTimeEventArgs? captured = null;
        service.OnTimeAdvanced += e => captured = e;

        service.Tick();

        captured.Should().NotBeNull();
        captured!.TotalMinutes.Should().Be(8 * 60 + 31);
        captured.CurrentHour.Should().Be(8);
        captured.CurrentMinute.Should().Be(31);
        captured.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void TimeScale_CanBeSet()
    {
        var state = new StateContainer();
        var service = CreateEnabled(state);
        service.TimeScale = 2.0f;
        service.TimeScale.Should().Be(2.0f);
    }

    [Fact]
    public void WhenDisabled_TotalMinutesReturnsZero()
    {
        var state = new StateContainer();
        var options = new LingFanEngineOptions { EnableTimeSystem = false };
        var service = new GameTimeService(state, options);

        service.TotalMinutes.Should().Be(0);
        service.IsPaused.Should().BeFalse();
        // 禁用时 Tick 无副作用
        service.Tick();
        service.TotalMinutes.Should().Be(0);
    }
}
