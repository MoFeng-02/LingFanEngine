using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// B1-c：OverlayRenderer Tier A 状态驱动测试（无宿主）。
/// <para>OverlayRenderer 是 internal sealed，构造器 (IStateContainer, II18nService?)；
/// 经 Attach(sceneRoot, outerGrid, dialogMask) 后由 Update(delta) 状态驱动增删覆盖层。
/// 测试全部通过可观察的容器 Children 断言（不触碰私有字段），覆盖菜单/输入/Toast/性能HUD/对话遮罩五路分支。</para>
/// </summary>
public class OverlayRendererTests
{
    private sealed class Harness
    {
        public StateContainer State { get; } = new();
        public Panel SceneRoot { get; } = new();
        public Grid OuterGrid { get; } = new();
        public Border DialogMask { get; } = new();
        public OverlayRenderer Renderer { get; }

        public Harness()
        {
            Renderer = new OverlayRenderer(State);
            Renderer.Attach(SceneRoot, OuterGrid, DialogMask);
        }

        public Border? NotifyToast =>
            OuterGrid.Children.OfType<Border>()
                .FirstOrDefault(b => b.Tag?.ToString() == StateKeys.UiTags.Notify);
    }

    // ========== 菜单覆盖层 ==========

    [Fact]
    public void Menu_OptionsPresent_AddsMenuPanel()
    {
        var h = new Harness();
        h.State.Set<object>(StateKeys.Menu.Options, new[] { "A", "B" });
        h.State.Set(StateKeys.Menu.Prompt, "选择");

        h.Renderer.Update(0.016);

        h.SceneRoot.Children.OfType<Panel>().Should().ContainSingle();
    }

    [Fact]
    public void Menu_OptionsCleared_RemovesMenuPanel()
    {
        var h = new Harness();
        h.State.Set<object>(StateKeys.Menu.Options, new[] { "A" });
        h.Renderer.Update(0.016);
        h.SceneRoot.Children.OfType<Panel>().Should().ContainSingle();

        // 清空选项 → 下一帧移除
        h.State.Set<object?>(StateKeys.Menu.Options, null);
        h.Renderer.Update(0.016);
        h.SceneRoot.Children.OfType<Panel>().Should().BeEmpty();
    }

    [Fact]
    public void Menu_UpdateTwice_DoesNotDuplicatePanel()
    {
        var h = new Harness();
        h.State.Set<object>(StateKeys.Menu.Options, new[] { "A", "B" });
        h.Renderer.Update(0.016);
        h.Renderer.Update(0.016);
        h.SceneRoot.Children.OfType<Panel>().Should().ContainSingle();
    }

    // ========== 输入覆盖层 ==========

    [Fact]
    public void Input_PromptPresent_AddsInputPanel_AndDoesNotThrow()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Input.Prompt, "请输入名字");

        // 潜在雷点：UpdateInputOverlay 内部调用 _inputBox.Focus()（无 try-catch）。
        // 无宿主时应静默 no-op 而非抛异常——此断言即验证该行为契约。
        var act = () => h.Renderer.Update(0.016);
        act.Should().NotThrow();
        h.SceneRoot.Children.OfType<Panel>().Should().ContainSingle();
    }

    [Fact]
    public void Input_PromptCleared_RemovesInputPanel()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Input.Prompt, "请输入");
        h.Renderer.Update(0.016);
        h.SceneRoot.Children.OfType<Panel>().Should().ContainSingle();

        h.State.Set<object?>(StateKeys.Input.Prompt, null);
        h.Renderer.Update(0.016);
        h.SceneRoot.Children.OfType<Panel>().Should().BeEmpty();
    }

    // ========== 通知 Toast ==========

    [Theory]
    [InlineData("info", "[i]")]
    [InlineData("warning", "[!]")]
    [InlineData("error", "[X]")]
    public void Notify_TypeMapsToIcon(string type, string icon)
    {
        var h = new Harness();
        h.State.Set(StateKeys.Notify.Text, "消息内容");
        h.State.Set(StateKeys.Notify.Type, type);

        h.Renderer.Update(0.016);

        h.NotifyToast.Should().NotBeNull();
        var tb = (TextBlock)h.NotifyToast!.Child!;
        tb.Text.Should().Contain(icon);
        tb.Text.Should().Contain("消息内容");
    }

    [Fact]
    public void Notify_UnknownType_FallsBackToInfoIcon()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Notify.Text, "x");
        h.State.Set(StateKeys.Notify.Type, "totally-unknown");
        h.Renderer.Update(0.016);
        ((TextBlock)h.NotifyToast!.Child!).Text.Should().Contain("[i]");
    }

    [Fact]
    public void Notify_ClearsStateAfterShowing()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Notify.Text, "hi");
        h.State.Set(StateKeys.Notify.Type, "info");
        h.Renderer.Update(0.016);

        // 显示后应清空触发键，避免下一帧重复创建
        h.State.Get<string>(StateKeys.Notify.Text).Should().BeNull();
    }

    [Fact]
    public void Notify_FadeLifecycle_RemovesToastThenDequeuesNext()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Notify.Text, "first");
        h.State.Set(StateKeys.Notify.Type, "info");
        h.State.Set(StateKeys.Notify.Duration, 0.1);
        // 预置队列：第一条淡出后应自动出队第二条
        h.State.Set(StateKeys.Notify.Queue, new List<NotificationItem>
        {
            new() { Text = "second", Type = "warning", Duration = 3.0 }
        });

        h.Renderer.Update(0.016);          // 显示 first（remain=0.1, fade=0.3）
        h.NotifyToast.Should().NotBeNull();

        h.Renderer.Update(1.0);            // remain 归零 → 进入淡出阶段
        h.Renderer.Update(1.0);            // 淡出完成 → 移除 + DequeueNextNotify 设置 second

        // 第一条已移除，队列已把 second 写回 Notify.Text
        h.State.Get<string>(StateKeys.Notify.Text).Should().Be("second");

        h.Renderer.Update(0.016);          // 下一帧显示 second
        ((TextBlock)h.NotifyToast!.Child!).Text.Should().Contain("second");
    }

    // ========== 性能 HUD ==========

    [Fact]
    public void PerfHud_ShowHudTrue_AddsHudWithFpsText()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Performance.ShowHud, true);
        h.State.Set(StateKeys.Performance.Fps, 60.0);

        h.Renderer.Update(0.016);

        var hud = h.OuterGrid.Children.OfType<TextBlock>().SingleOrDefault();
        hud.Should().NotBeNull();
        hud!.Text.Should().Contain("FPS");
        hud.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void PerfHud_TextUpdatesWhenFpsChanges()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Performance.ShowHud, true);
        h.State.Set(StateKeys.Performance.Fps, 30.0);
        h.Renderer.Update(0.016);
        var hud = h.OuterGrid.Children.OfType<TextBlock>().Single();
        var first = hud.Text;

        h.State.Set(StateKeys.Performance.Fps, 120.0);
        h.Renderer.Update(0.016);
        hud.Text.Should().NotBe(first);
        hud.Text.Should().Contain("120");
    }

    [Fact]
    public void PerfHud_ShowHudFalse_HidesHud()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Performance.ShowHud, true);
        h.Renderer.Update(0.016);
        h.OuterGrid.Children.OfType<TextBlock>().Single().IsVisible.Should().BeTrue();

        h.State.Set(StateKeys.Performance.ShowHud, false);
        h.Renderer.Update(0.016);
        h.OuterGrid.Children.OfType<TextBlock>().Single().IsVisible.Should().BeFalse();
    }

    // ========== 对话模态遮罩 ==========

    [Theory]
    [InlineData("dialog")]
    [InlineData("wait_skipable")]
    [InlineData("pause")]
    public void DialogMask_VisibleForWaitingTypes_WhenNotClickable(string waitingType)
    {
        var h = new Harness();
        h.State.Set(StateKeys.Dsl.WaitingType, waitingType);
        h.State.Set(StateKeys.Dialog.Clickable, false);
        h.Renderer.Update(0.016);
        h.DialogMask.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void DialogMask_HiddenWhenClickable()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Dsl.WaitingType, "dialog");
        h.State.Set(StateKeys.Dialog.Clickable, true);
        h.Renderer.Update(0.016);
        h.DialogMask.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void DialogMask_HiddenForNonBlockingWaitingType()
    {
        var h = new Harness();
        h.State.Set(StateKeys.Dsl.WaitingType, "");
        h.Renderer.Update(0.016);
        h.DialogMask.IsVisible.Should().BeFalse();
    }
}
