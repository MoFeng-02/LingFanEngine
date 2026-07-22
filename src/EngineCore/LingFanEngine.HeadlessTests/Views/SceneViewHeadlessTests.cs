using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Tests.Fakes;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// B2：SceneView headless 渲染契约（Tier B）。
/// <para>用 HeadlessUnitTestSession 提供真实 Avalonia 布局服务（IFontManagerImpl 等），构建真实场景视觉树，
/// 断言 RebuildScene 真的产出 _sceneRoot/_transitionOverlay/_scaleWrapper/_outerGrid，以及
/// UpdateTransition / UpdateShake / UpdateLayoutScale 三个渲染方法真的改写视觉属性。</para>
/// <para>隔离：注入 fake IOverlayRenderer/IVideoPresenter/IAnimationApplier/IInteractionBinder（避免 GpuMediaPlayer 与
/// new Cursor(Hand)）；dialogBoxFactory/dialogRegistry 留 null → 回退真实 DialogBox（安全，仅属性）。
/// 引擎零改动、AOT 安全、仅测试工程引用 Headless。</para>
/// </summary>
public class SceneViewHeadlessTests
{
    private sealed class FakeI18n : II18nService
    {
        public string Translate(string original) => original;
        public void SwitchLanguage(string lang) { }
        public IReadOnlyList<string> GetAvailableLanguages() => new[] { "zh-CN" };
    }

    private sealed class FakeOverlayRenderer : IOverlayRenderer
    {
        public Panel? AttachedSceneRoot { get; private set; }
        public void Attach(Panel? sceneRoot, Grid? outerGrid, Border? dialogMask) => AttachedSceneRoot = sceneRoot;
        public void Detach() => AttachedSceneRoot = null;
        public void Update(double delta) { }
    }

    private sealed class FakeVideoPresenter : IVideoPresenter
    {
        public void Attach(Panel? sceneRoot, Grid? outerGrid) { }
        public void Detach() { }
        public void Update() { }
    }

    private sealed class FakeAnimationApplier : IAnimationApplier
    {
        public void Apply(Panel? sceneRoot) { }
        public void RebuildControlMap(Panel? sceneRoot) { }
    }

    private sealed class FakeInteractionBinder : IInteractionBinder
    {
        public void ApplyInteraction(Control control, Dictionary<string, object> props) { }
    }

    private static SceneView BuildScene(StateContainer state, LayoutScaleMode scaleMode)
    {
        var pipeline = new FakeCommandPipeline();
        var i18n = new FakeI18n();
        var controlFactory = new ControlFactory(i18n, state); // 真实 ControlFactory：(II18nService, IStateContainer)
        var interactionBinder = new FakeInteractionBinder();
        var overlay = new FakeOverlayRenderer();
        var video = new FakeVideoPresenter();
        var anim = new FakeAnimationApplier();

        return new SceneView(state, pipeline, i18n, controlFactory, interactionBinder,
            overlay, video, anim, designWidth: 1920, designHeight: 1080, scaleMode: scaleMode);
    }

    [Fact]
    public void RebuildScene_BuildsVisualTree_OuterGrid_ScaleWrapper_SceneRoot_TransitionOverlay()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentName, "test");
        state.Set(StateKeys.Scene.Elements, new List<UIElementEntity>
        {
            new() { ElementType = "text", Order = 0, Properties = new() { ["text"] = "标题" } }
        });

        var session = HeadlessSession.Instance;
        session.Dispatch(() =>
        {
            var scene = BuildScene(state, LayoutScaleMode.Stretch);
            scene.Measure(new Size(1280, 720));
            scene.Arrange(new Rect(0, 0, 1280, 720));
            scene.Update(0.016);

            // 外层 Grid（Content）
            var outer = scene.Content.Should().BeOfType<Grid>().Subject;
            // 含缩放层 Grid 与过渡遮罩 Border(ZIndex=200)
            var scaleWrapper = outer.Children.OfType<Grid>().Should().ContainSingle().Subject;
            var transitionOverlay = outer.Children.OfType<Border>()
                .First(b => b.ZIndex == 200);
            transitionOverlay.Should().NotBeNull();
            transitionOverlay.IsVisible.Should().BeFalse(); // 过渡未激活

            // 缩放层内含场景根 Panel
            var sceneRoot = scaleWrapper.Children.OfType<Panel>().Should().ContainSingle().Subject;
            // 场景根：1 个文本元素 + 对话遮罩(Border ZIndex=50) + 对话框(UserControl)
            sceneRoot.Children.Should().HaveCount(3);
            sceneRoot.Children.Should().ContainSingle(c => c is TextBlock);
            sceneRoot.Children.OfType<Border>().Should().ContainSingle(b => b.ZIndex == 50);
            sceneRoot.Children.Should().ContainSingle(c => c is UserControl);
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void UpdateTransition_ActiveProgress_ShowsBlackOverlay_WithFadingAlpha()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentName, "test");
        state.Set(StateKeys.Scene.Elements, new List<UIElementEntity>
        {
            new() { ElementType = "text", Order = 0, Properties = new() { ["text"] = "x" } }
        });

        var session = HeadlessSession.Instance;
        session.Dispatch(() =>
        {
            var scene = BuildScene(state, LayoutScaleMode.Stretch);
            scene.Measure(new Size(1280, 720));
            scene.Arrange(new Rect(0, 0, 1280, 720));
            scene.Update(0.016);

            var outer = (Grid)scene.Content!;
            var transitionOverlay = outer.Children.OfType<Border>().First(b => b.ZIndex == 200);

            // 激活过渡，progress=0.5 → alpha=(1-0.5)*255=127
            state.Set(StateKeys.Transition.Active, true);
            state.Set(StateKeys.Transition.Progress, 0.5);
            scene.Update(0.016);

            transitionOverlay.IsVisible.Should().BeTrue();
            var brush = transitionOverlay.Background.Should().BeOfType<SolidColorBrush>().Subject;
            brush.Color.A.Should().Be(127);
            brush.Color.R.Should().Be(0);
            brush.Color.G.Should().Be(0);
            brush.Color.B.Should().Be(0);

            // 结束过渡 → 遮罩隐藏
            state.Set(StateKeys.Transition.Active, false);
            scene.Update(0.016);
            transitionOverlay.IsVisible.Should().BeFalse();
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void UpdateShake_Active_SetsSceneRootTranslateTransform()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentName, "test");
        state.Set(StateKeys.Scene.Elements, new List<UIElementEntity>
        {
            new() { ElementType = "text", Order = 0, Properties = new() { ["text"] = "x" } }
        });

        var session = HeadlessSession.Instance;
        session.Dispatch(() =>
        {
            var scene = BuildScene(state, LayoutScaleMode.Stretch);
            scene.Measure(new Size(1280, 720));
            scene.Arrange(new Rect(0, 0, 1280, 720));
            scene.Update(0.016);

            var outer = (Grid)scene.Content!;
            var sceneRoot = outer.Children.OfType<Grid>().Single()
                .Children.OfType<Panel>().Single();

            state.Set(StateKeys.Shake.Active, true);
            state.Set(StateKeys.Shake.OffsetX, 10.0);
            state.Set(StateKeys.Shake.OffsetY, 20.0);
            scene.Update(0.016);

            var tt = sceneRoot.RenderTransform.Should().BeOfType<TranslateTransform>().Subject;
            tt.X.Should().Be(10.0);
            tt.Y.Should().Be(20.0);

            // 停止震动 → 渲染变换置空
            state.Set(StateKeys.Shake.Active, false);
            scene.Update(0.016);
            sceneRoot.RenderTransform.Should().BeNull();
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void UpdateShake_WithTransitionActive_ReusesTransformGroup_AddsShakeOffset()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentName, "test");
        state.Set(StateKeys.Scene.Elements, new List<UIElementEntity>
        {
            new() { ElementType = "text", Order = 0, Properties = new() { ["text"] = "x" } }
        });

        var session = HeadlessSession.Instance;
        session.Dispatch(() =>
        {
            var scene = BuildScene(state, LayoutScaleMode.Stretch);
            scene.Measure(new Size(1280, 720));
            scene.Arrange(new Rect(0, 0, 1280, 720));
            scene.Update(0.016);

            var outer = (Grid)scene.Content!;
            var sceneRoot = outer.Children.OfType<Grid>().Single()
                .Children.OfType<Panel>().Single();

            state.Set(StateKeys.Transition.Active, true);
            state.Set(StateKeys.Transition.Progress, 0.5);
            state.Set(StateKeys.Transition.OffsetX, 0.0);
            state.Set(StateKeys.Transition.OffsetY, 0.0);
            state.Set(StateKeys.Transition.Scale, 1.0);
            state.Set(StateKeys.Shake.Active, true);
            state.Set(StateKeys.Shake.OffsetX, 10.0);
            state.Set(StateKeys.Shake.OffsetY, 20.0);
            scene.Update(0.016);

            var group = sceneRoot.RenderTransform.Should().BeOfType<TransformGroup>().Subject;
            var shake = group.Children.OfType<TranslateTransform>().Should().ContainSingle().Subject;
            shake.X.Should().Be(10.0);
            shake.Y.Should().Be(20.0);
        }, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void UpdateLayoutScale_ContainMode_ScalesByMinRatio()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentName, "test");
        state.Set(StateKeys.Scene.Elements, new List<UIElementEntity>
        {
            new() { ElementType = "text", Order = 0, Properties = new() { ["text"] = "x" } }
        });

        var session = HeadlessSession.Instance;
        session.Dispatch(() =>
        {
            var scene = BuildScene(state, LayoutScaleMode.Contain);
            // 1280x720 显示区，1920x1080 设计区 → Contain=min(1280/1920,720/1080)=0.667
            scene.Measure(new Size(1280, 720));
            scene.Arrange(new Rect(0, 0, 1280, 720));
            scene.Update(0.016);

            var outer = (Grid)scene.Content!;
            var scaleWrapper = outer.Children.OfType<Grid>().Should().ContainSingle().Subject;
            var st = scaleWrapper.RenderTransform.Should().BeOfType<ScaleTransform>().Subject;
            st.ScaleX.Should().BeApproximately(1280.0 / 1920.0, 0.001);
            st.ScaleY.Should().BeApproximately(720.0 / 1080.0, 0.001);
        }, CancellationToken.None).GetAwaiter().GetResult();
    }
}
