using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.Transitions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// TransitionEngine 纯逻辑契约测试（F5/F11 补强）。
/// <para>过渡引擎完全状态驱动、无 Avalonia 依赖，可脱离 headless 宿主直接验证：
/// StartTransition 写入初值、Update 逐帧按类型+缓动推进 __transition_* 状态、t≥1 自动收尾、
/// CompleteTransition 立即归位。缓动 ApplyEasing 为私有静态，用 Linear 缓动经 Update 间接验证数值。</para>
/// </summary>
public class TransitionEngineTests
{
    private static (TransitionEngine engine, StateContainer state) Create(
        int w = 1000, int h = 800)
    {
        var state = new StateContainer();
        var opt = new LingFanEngineOptions { WindowWidth = w, WindowHeight = h };
        return (new TransitionEngine(state, opt), state);
    }

    private static TransitionEntity T(TransitionType type, double duration = 1.0,
        EasingType easing = EasingType.Linear)
        => new() { Type = type, Duration = duration, Easing = easing };

    // ========== StartTransition ==========

    [Fact]
    public void StartTransition_WritesInitialState()
    {
        var (engine, state) = Create();

        engine.StartTransition(T(TransitionType.FadeIn, 0.4, EasingType.EaseInQuad));

        engine.IsActive.Should().BeTrue();
        state.Get<bool>(StateKeys.Transition.Active).Should().BeTrue();
        state.Get<string>(StateKeys.Transition.Type).Should().Be("FadeIn");
        state.Get<double>(StateKeys.Transition.Progress).Should().Be(0.0);
        state.Get<double>(StateKeys.Transition.Duration).Should().Be(0.4);
        state.Get<string>(StateKeys.Transition.Easing).Should().Be("EaseInQuad");
        state.Get<double>(StateKeys.Transition.Elapsed).Should().Be(0.0);
        state.Get<double>(StateKeys.Transition.Scale).Should().Be(1.0);
    }

    [Fact]
    public void StartTransition_Null_UsesCrossFadeDefaults()
    {
        var (engine, state) = Create();

        engine.StartTransition(null);

        state.Get<string>(StateKeys.Transition.Type).Should().Be("CrossFade");
        state.Get<double>(StateKeys.Transition.Duration).Should().Be(0.5);
        state.Get<string>(StateKeys.Transition.Easing).Should().Be("EaseOutQuad");
    }

    // ========== Update: 非活跃 no-op ==========

    [Fact]
    public void Update_WhenInactive_DoesNothing()
    {
        var (engine, state) = Create();
        // 未 StartTransition，Active 默认 false

        engine.Update(0.5);

        state.ContainsKey(StateKeys.Transition.Elapsed).Should().BeFalse();
        state.Get<double>(StateKeys.Transition.Progress).Should().Be(0.0);
    }

    // ========== Update: 各类型数值（Linear 缓动，t=elapsed/duration） ==========

    [Fact]
    public void Update_FadeIn_LinearProgressEqualsT()
    {
        var (engine, state) = Create();
        engine.StartTransition(T(TransitionType.FadeIn, 1.0));

        engine.Update(0.5); // t=0.5，Linear→eased=0.5

        state.Get<double>(StateKeys.Transition.Progress).Should().BeApproximately(0.5, 1e-9);
        state.Get<double>(StateKeys.Transition.Elapsed).Should().BeApproximately(0.5, 1e-9);
        state.Get<bool>(StateKeys.Transition.Active).Should().BeTrue();
    }

    [Fact]
    public void Update_FadeOut_ProgressIsInverse()
    {
        var (engine, state) = Create();
        engine.StartTransition(T(TransitionType.FadeOut, 1.0));

        engine.Update(0.25); // t=0.25，Progress = 1 - 0.25 = 0.75

        state.Get<double>(StateKeys.Transition.Progress).Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public void Update_ZoomIn_SetsScaleAndProgress()
    {
        var (engine, state) = Create();
        engine.StartTransition(T(TransitionType.ZoomIn, 1.0));

        engine.Update(0.5); // eased=0.5 → Scale=0.5+0.5*0.5=0.75

        state.Get<double>(StateKeys.Transition.Scale).Should().BeApproximately(0.75, 1e-9);
        state.Get<double>(StateKeys.Transition.Progress).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Update_SlideLeftIn_SetsOffsetX()
    {
        var (engine, state) = Create(w: 1000);
        engine.StartTransition(T(TransitionType.SlideLeftIn, 1.0));

        engine.Update(0.5); // OffsetX = -1000 + 1000*0.5 = -500

        state.Get<double>(StateKeys.Transition.OffsetX).Should().BeApproximately(-500, 1e-6);
    }

    [Fact]
    public void Update_SlideDownOut_SetsOffsetY()
    {
        var (engine, state) = Create(h: 800);
        engine.StartTransition(T(TransitionType.SlideDownOut, 1.0));

        engine.Update(0.25); // OffsetY = 800 * 0.25 = 200

        state.Get<double>(StateKeys.Transition.OffsetY).Should().BeApproximately(200, 1e-6);
    }

    [Fact]
    public void Update_ReachingEnd_DeactivatesAndResets()
    {
        var (engine, state) = Create();
        engine.StartTransition(T(TransitionType.SlideLeftIn, 1.0));

        engine.Update(1.0); // t=1.0 → 收尾分支

        engine.IsActive.Should().BeFalse();
        state.Get<bool>(StateKeys.Transition.Active).Should().BeFalse();
        state.Get<double>(StateKeys.Transition.Progress).Should().Be(1.0);
        state.Get<double>(StateKeys.Transition.OffsetX).Should().Be(0.0);
        state.Get<double>(StateKeys.Transition.OffsetY).Should().Be(0.0);
        state.Get<double>(StateKeys.Transition.Scale).Should().Be(1.0);
        state.Get<double>(StateKeys.Transition.Elapsed).Should().Be(0.0);
        state.ContainsKey(StateKeys.Transition.Type).Should().BeFalse();
        state.ContainsKey(StateKeys.Transition.Easing).Should().BeFalse();
        state.ContainsKey(StateKeys.Transition.Duration).Should().BeFalse();
    }

    [Fact]
    public void Update_ZeroDuration_CompletesImmediately()
    {
        var (engine, state) = Create();
        engine.StartTransition(T(TransitionType.FadeIn, 0.0)); // duration=0 → t=1.0

        engine.Update(0.016);

        engine.IsActive.Should().BeFalse();
        state.Get<double>(StateKeys.Transition.Progress).Should().Be(1.0);
    }

    [Fact]
    public void Update_EaseInQuad_AppliesEasingCurve()
    {
        // EaseInQuad: eased = t*t。t=0.5 → 0.25，可与 Linear 区分，证明缓动被消费。
        var (engine, state) = Create();
        engine.StartTransition(T(TransitionType.FadeIn, 1.0, EasingType.EaseInQuad));

        engine.Update(0.5);

        state.Get<double>(StateKeys.Transition.Progress).Should().BeApproximately(0.25, 1e-9);
    }

    // ========== CompleteTransition ==========

    [Fact]
    public void CompleteTransition_ResetsAllState()
    {
        var (engine, state) = Create();
        engine.StartTransition(T(TransitionType.ZoomIn, 5.0));
        engine.Update(0.5); // 中途

        engine.CompleteTransition();

        engine.IsActive.Should().BeFalse();
        state.Get<double>(StateKeys.Transition.Progress).Should().Be(1.0);
        state.Get<double>(StateKeys.Transition.Scale).Should().Be(1.0);
        state.Get<double>(StateKeys.Transition.Elapsed).Should().Be(0.0);
        state.ContainsKey(StateKeys.Transition.Type).Should().BeFalse();
    }
}
