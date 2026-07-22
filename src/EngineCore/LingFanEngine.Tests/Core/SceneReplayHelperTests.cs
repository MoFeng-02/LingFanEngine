using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// SceneReplayHelper 纯逻辑契约测试（F5/F11 补强）。
/// <para>回放辅助器从命令序列 [0, upToIndex) 重建 Scene.Elements / RuntimeElements / CurrentBackground，
/// 完全无 Avalonia 依赖。逐命令类型验证与对应 Handler 行为一致：ShowElement 追加场景元素；
/// background/bg_switch 替换背景（Order=-1000）；show/hide 图片；sprite show/hide/move/state；
/// live2d show/hide；以及 upToIndex 截断与 Dirty 标记。</para>
/// </summary>
public class SceneReplayHelperTests
{
    private static List<UIElementEntity> Scene(IStateContainer s)
        => s.Get<List<UIElementEntity>>(StateKeys.Scene.Elements) ?? new();

    private static List<UIElementEntity> Runtime(IStateContainer s)
        => s.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements) ?? new();

    private static UIElementEntity? ByTag(List<UIElementEntity> list, string tag)
        => list.FirstOrDefault(e =>
            e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) && t?.ToString() == tag);

    // ========== ShowElement（场景定义元素） ==========

    [Fact]
    public void ReplaysShowElement_IntoSceneElements()
    {
        var state = new StateContainer();
        var el = new UIElementEntity { ElementType = "text", Properties = new() { ["text"] = "hi" } };
        var cmds = new List<ICommand> { new ShowElementCommand { Element = el } };

        var count = SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        count.Should().Be(1);
        Scene(state).Should().ContainSingle().Which.ElementType.Should().Be("text");
        state.Get<bool>(StateKeys.Scene.Dirty).Should().BeTrue();
    }

    // ========== background / bg_switch ==========

    [Fact]
    public void ReplaysBackground_SetsCurrentBgAndRuntimeElement()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new ShowHideCommand { Target = "bg1.png", IsBackground = true, IsShow = true }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        state.Get<string>(StateKeys.Scene.CurrentBackground).Should().Be("bg1.png");
        var bg = Runtime(state).Single();
        bg.ElementType.Should().Be("background");
        bg.Order.Should().Be(-1000);
        bg.Properties["source"].Should().Be("bg1.png");
    }

    [Fact]
    public void ReplaysBackground_ReplacesPreviousBackground()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new ShowHideCommand { Target = "old.png", IsBackground = true, IsShow = true },
            new ShowHideCommand { Target = "new.png", IsBackground = true, IsShow = true }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        var backgrounds = Runtime(state).Where(e => e.ElementType == "background").ToList();
        backgrounds.Should().ContainSingle();
        backgrounds[0].Properties["source"].Should().Be("new.png");
        state.Get<string>(StateKeys.Scene.CurrentBackground).Should().Be("new.png");
    }

    [Fact]
    public void ReplaysBgSwitch_SwitchesBackground()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new ShowHideCommand { Target = "old.png", IsBackground = true, IsShow = true },
            new BgSwitchCommand { Path = "switched.png" }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        Runtime(state).Where(e => e.ElementType == "background").Should().ContainSingle()
            .Which.Properties["source"].Should().Be("switched.png");
        state.Get<string>(StateKeys.Scene.CurrentBackground).Should().Be("switched.png");
    }

    // ========== show / hide 图片 ==========

    [Fact]
    public void ReplaysShowImage_AddsRuntimeImage()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new ShowHideCommand { Target = "hero.png", X = 100, Y = 50, IsShow = true, Tag = "hero" }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        var img = Runtime(state).Single();
        img.ElementType.Should().Be("image");
        img.Properties["source"].Should().Be("hero.png");
        img.Properties["x"].Should().Be(100.0);
        img.Properties[StateKeys.UiTags.Tag].Should().Be("hero");
    }

    [Fact]
    public void ReplaysHideByTarget_RemovesImage()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new ShowHideCommand { Target = "hero.png", IsShow = true },
            new ShowHideCommand { Target = "hero.png", IsShow = false }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        Runtime(state).Should().BeEmpty();
    }

    [Fact]
    public void ReplaysHideByTag_RemovesImage()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new ShowHideCommand { Target = "a.png", IsShow = true, Tag = "npc" },
            new ShowHideCommand { Target = "", IsShow = false, Tag = "npc" }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        Runtime(state).Should().BeEmpty();
    }

    // ========== sprite ==========

    [Fact]
    public void ReplaysSpriteShow_AddsAndReplacesById()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new SpriteCommand { Operation = "show", Id = "alice", Source = "alice_a.png", X = 10, Y = 20 },
            new SpriteCommand { Operation = "show", Id = "alice", Source = "alice_b.png" } // 同 ID 替换
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        var alice = Runtime(state).Where(e => ByTag(new() { e }, "alice") != null).ToList();
        alice.Should().ContainSingle();
        alice[0].Properties["source"].Should().Be("alice_b.png");
    }

    [Fact]
    public void ReplaysSpriteMove_UpdatesPosition()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new SpriteCommand { Operation = "show", Id = "bob", Source = "bob.png", X = 0, Y = 0 },
            new SpriteCommand { Operation = "move", Id = "bob", X = 300, Y = 150 }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        var bob = ByTag(Runtime(state), "bob")!;
        bob.Properties["x"].Should().Be(300.0);
        bob.Properties["y"].Should().Be(150.0);
    }

    [Fact]
    public void ReplaysSpriteState_UpdatesEmotionSource()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new SpriteCommand { Operation = "show", Id = "carol", Source = "carol_normal.png" },
            new SpriteCommand { Operation = "state", Id = "carol", Emotion = "carol_happy.png" }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        ByTag(Runtime(state), "carol")!.Properties["source"].Should().Be("carol_happy.png");
    }

    [Fact]
    public void ReplaysSpriteHide_RemovesById()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new SpriteCommand { Operation = "show", Id = "dave", Source = "dave.png" },
            new SpriteCommand { Operation = "hide", Id = "dave" }
        };

        SceneReplayHelper.ReplaySceneState(cmds, cmds.Count, state);

        Runtime(state).Should().BeEmpty();
    }

    // ========== live2d ==========

    [Fact]
    public void ReplaysLive2DShowThenHide()
    {
        var state = new StateContainer();
        var showOnly = new List<ICommand> { new Live2DCommand { Operation = "show", Id = "hime" } };
        SceneReplayHelper.ReplaySceneState(showOnly, showOnly.Count, state);
        var l2d = Runtime(state).Single();
        l2d.ElementType.Should().Be("Live2D");
        l2d.Properties["modelId"].Should().Be("hime");

        var withHide = new List<ICommand>
        {
            new Live2DCommand { Operation = "show", Id = "hime" },
            new Live2DCommand { Operation = "hide", Id = "hime" }
        };
        SceneReplayHelper.ReplaySceneState(withHide, withHide.Count, state);
        Runtime(state).Should().BeEmpty();
    }

    // ========== upToIndex 截断 ==========

    [Fact]
    public void RespectsUpToIndex_OnlyReplaysPrefix()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand>
        {
            new ShowHideCommand { Target = "a.png", IsShow = true },
            new ShowHideCommand { Target = "b.png", IsShow = true },
            new ShowHideCommand { Target = "c.png", IsShow = true }
        };

        SceneReplayHelper.ReplaySceneState(cmds, 2, state); // 只回放前 2 条

        var sources = Runtime(state).Select(e => e.Properties["source"].ToString()).ToList();
        sources.Should().BeEquivalentTo(new[] { "a.png", "b.png" });
    }

    [Fact]
    public void UpToIndex_ClampsToCommandCount()
    {
        var state = new StateContainer();
        var cmds = new List<ICommand> { new ShowHideCommand { Target = "a.png", IsShow = true } };

        // upToIndex 超过数量不越界
        var act = () => SceneReplayHelper.ReplaySceneState(cmds, 999, state);

        act.Should().NotThrow();
        Runtime(state).Should().ContainSingle();
    }
}
