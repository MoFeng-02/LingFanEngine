using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// AnimationService 控件动画服务测试
/// <para>覆盖线性插值、动画完成清理、EaseOutQuad 缓动、重复次数递减，以及无活跃动画时的空操作。</para>
/// <para>仅测纯数学推进逻辑，不涉及渲染。</para>
/// </summary>
public class AnimationServiceTests
{
    private static string Key(string name, string suffix) => StateKeys.Animation.Prefix + name + suffix;

    private static void Setup(StateContainer state, string name, double from, double target, double duration,
        double elapsed, string easing, bool active, int repeat = 0)
    {
        var p = StateKeys.Animation.Prefix + name;
        state.Set(p + StateKeys.Animation.FromSuffix, from);
        state.Set(p + StateKeys.Animation.TargetSuffix, target);
        state.Set(p + StateKeys.Animation.DurationSuffix, duration);
        state.Set(p + StateKeys.Animation.ElapsedSuffix, elapsed);
        state.Set(p + StateKeys.Animation.EasingSuffix, easing);
        state.Set(p + StateKeys.Animation.ActiveSuffix, active);
        state.Set(p + StateKeys.Animation.CurrentSuffix, from);
        if (repeat != 0)
            state.Set(p + StateKeys.Animation.RepeatSuffix, repeat);
    }

    [Fact]
    public void Update_LinearProgress_InterpolatesCurrent()
    {
        var state = new StateContainer();
        Setup(state, "t", 0, 100, 10, 0, "Linear", true);

        new AnimationService().Update(2.0, state);

        state.Get<double>(Key("t", StateKeys.Animation.CurrentSuffix)).Should().BeApproximately(20.0, 0.0001);
    }

    [Fact]
    public void Update_Completes_WhenElapsedExceedsDuration()
    {
        var state = new StateContainer();
        Setup(state, "c", 0, 100, 10, 8, "Linear", true);

        new AnimationService().Update(5.0, state);

        state.Get<bool>(Key("c", StateKeys.Animation.ActiveSuffix)).Should().BeFalse();
        state.ContainsKey(Key("c", StateKeys.Animation.CurrentSuffix)).Should().BeFalse();
        state.ContainsKey(Key("c", StateKeys.Animation.FromSuffix)).Should().BeFalse();
    }

    [Fact]
    public void Update_EaseOutQuad_AtHalf_IsThreeQuarter()
    {
        var state = new StateContainer();
        Setup(state, "e", 0, 100, 10, 5, "EaseOutQuad", true);

        new AnimationService().Update(0, state);

        // t=0.5, EaseOutQuad: t*(2-t)=0.75 → 100*0.75=75
        state.Get<double>(Key("e", StateKeys.Animation.CurrentSuffix)).Should().BeApproximately(75.0, 0.0001);
    }

    [Fact]
    public void Update_RepeatRemaining_ResetsAndDecrements()
    {
        var state = new StateContainer();
        Setup(state, "r", 0, 100, 10, 10, "Linear", true, repeat: 2);

        new AnimationService().Update(0, state);

        state.Get<bool>(Key("r", StateKeys.Animation.ActiveSuffix)).Should().BeTrue();
        state.Get<double>(Key("r", StateKeys.Animation.ElapsedSuffix)).Should().BeApproximately(0.0, 0.0001);
        state.Get<int>(Key("r", StateKeys.Animation.RepeatSuffix)).Should().Be(1);
        state.Get<double>(Key("r", StateKeys.Animation.CurrentSuffix)).Should().BeApproximately(100.0, 0.0001);
    }

    [Fact]
    public void Update_RepeatPingPong_SecondPassMovesBackTowardStart()
    {
        // 回归（2026-09 repeat 动画静止 BUG）：旧实现循环时 From=Target 且 Target 不变，
        // 第二轮起 current 恒 = target（补间区间长度为 0）——repeat 动画第一轮后完全静止。
        // 修复：往返（ping-pong）——交换 from/target，第二轮从 target 回到起点。
        var state = new StateContainer();
        Setup(state, "p", 0, 100, 10, 10, "Linear", true, repeat: -1); // 无限循环

        var svc = new AnimationService();
        svc.Update(0, state); // 第一轮结束 → 交换：from=100, target=0

        // 第二轮推进一半（elapsed 5 / duration 10，Linear）→ current = 100 + (0-100)*0.5 = 50
        svc.Update(5.0, state);
        state.Get<double>(Key("p", StateKeys.Animation.CurrentSuffix)).Should().BeApproximately(50.0, 0.0001);

        // 第二轮结束 → 再次交换回 from=0, target=100（无限循环不停止）
        svc.Update(5.0, state);
        state.Get<bool>(Key("p", StateKeys.Animation.ActiveSuffix)).Should().BeTrue();
        state.Get<double>(Key("p", StateKeys.Animation.FromSuffix)).Should().BeApproximately(0.0, 0.0001);
        state.Get<double>(Key("p", StateKeys.Animation.TargetSuffix)).Should().BeApproximately(100.0, 0.0001);
    }

    [Fact]
    public void Update_NoActiveAnimations_Noop()
    {
        var state = new StateContainer();
        state.Set("unrelated_key", 1);

        var act = () => new AnimationService().Update(1.0, state);

        act.Should().NotThrow();
    }
}
