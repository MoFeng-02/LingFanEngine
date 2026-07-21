using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Tweens;
using Xunit;

namespace LingFanEngine.Tests.Tweens;

public class TweenEngineTests
{
    [Fact]
    public void AddTween_WithoutDelay_GoesToActive()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 10, Duration = 1 });
        engine.ActiveCount.Should().Be(1);
    }

    [Fact]
    public void AddTween_WithDelay_GoesToPending()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 10, Duration = 1, Delay = 1 });
        engine.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void Update_LinearReachesTargetAtEnd()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 100, Duration = 1, Easing = EasingType.Linear });
        engine.Update(1.0, 1.0f);
        state.Get<double>("x").Should().BeApproximately(100, 1e-6);
    }

    [Fact]
    public void Update_LinearMidpoint_IsHalf()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 100, Duration = 1, Easing = EasingType.Linear });
        engine.Update(0.5, 1.0f);
        state.Get<double>("x").Should().BeApproximately(50, 1e-6);
    }

    [Fact]
    public void Update_AdvancesPastDuration_ThenRemoves()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 100, Duration = 1 });
        engine.Update(2.0, 1.0f);
        engine.ActiveCount.Should().Be(0);
        state.Get<double>("x").Should().BeApproximately(100, 1e-6);
    }

    [Fact]
    public void Update_DelayedTween_StartsAfterDelay()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 100, Duration = 1, Delay = 1 });
        engine.Update(0.5, 1.0f); // 仍在延迟中
        engine.ActiveCount.Should().Be(0);
        engine.Update(0.6, 1.0f); // 延迟累计 1.1s，转入活跃并推进到 0.6/1.0
        engine.ActiveCount.Should().Be(1);
        state.Get<double>("x").Should().BeApproximately(60, 1e-6);
    }

    [Fact]
    public void Update_TimeScale_AffectsProgress()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 100, Duration = 1, Easing = EasingType.Linear });
        engine.Update(1.0, 0.5f); // 缩放后 delta = 0.5 -> 一半
        state.Get<double>("x").Should().BeApproximately(50, 1e-6);
    }

    [Fact]
    public void Update_TwoDimensional_WritesY()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween
        {
            TargetKey = "x",
            TargetKeyY = "y",
            From = 0,
            To = 100,
            FromY = 0,
            ToY = 200,
            Duration = 1,
            Easing = EasingType.Linear
        });
        engine.Update(1.0, 1.0f);
        state.Get<double>("x").Should().BeApproximately(100, 1e-6);
        state.Get<double>("y").Should().BeApproximately(200, 1e-6);
    }

    [Fact]
    public void Clear_RemovesAllTweens()
    {
        var state = new StateContainer();
        var engine = new TweenEngine(state);
        engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 10, Duration = 1 });
        engine.AddTween(new Tween { TargetKey = "y", From = 0, To = 10, Duration = 1, Delay = 5 });
        engine.Clear();
        engine.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void Update_AllEasingTypes_ReachTargetAtEnd()
    {
        foreach (EasingType easing in Enum.GetValues<EasingType>())
        {
            var state = new StateContainer();
            var engine = new TweenEngine(state);
            engine.AddTween(new Tween { TargetKey = "x", From = 0, To = 1, Duration = 1, Easing = easing });
            engine.Update(1.0, 1.0f);
            state.Get<double>("x").Should().BeApproximately(1, 1e-6,
                $"缓动 {easing} 应在结束时抵达目标值");
        }
    }
}
