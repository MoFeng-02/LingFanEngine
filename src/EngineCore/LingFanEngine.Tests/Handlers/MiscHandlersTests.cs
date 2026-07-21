using System.Collections.Generic;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// MiscHandlers 集成测试：nav_to_label / merge_defines / build_scene。
/// </summary>
public class MiscHandlersTests
{
    [Fact]
    public void NavToLabelHandler_StartsFromLabel_WhenDslExecutorPresent()
    {
        var ctx = new FakeCommandContext { DslExecutor = new FakeDslExecutor() };
        new NavToLabelHandler().Handle(new NavToLabelCommand { TargetLabel = "prologue" }, ctx);
        (ctx.DslExecutor as FakeDslExecutor)!.LastLabel.Should().Be("prologue");
    }

    [Fact]
    public void NavToLabelHandler_NullDslExecutor_DoesNotThrow()
    {
        var ctx = new FakeCommandContext(); // DslExecutor 默认 null
        var act = () => new NavToLabelHandler().Handle(new NavToLabelCommand { TargetLabel = "x" }, ctx);
        act.Should().NotThrow();
    }

    [Fact]
    public void MergeDefinesHandler_MergesIntoState()
    {
        var ctx = new FakeCommandContext();
        new MergeDefinesHandler().Handle(
            new MergeDefinesCommand { Defines = new Dictionary<string, object?> { ["cfg_vol"] = 0.5, ["cfg_mute"] = false } }, ctx);
        ctx.State.Get<object>("cfg_vol").Should().Be(0.5);
        ctx.State.Get<object>("cfg_mute").Should().Be(false);
    }

    [Fact]
    public void BuildSceneHandler_SetsElementsAndActivatesScreen()
    {
        var ctx = new FakeCommandContext();
        var el = new UIElementEntity { ElementType = "image", Properties = new Dictionary<string, object> { ["source"] = "bg.png" } };
        new BuildSceneHandler().Handle(
            new BuildSceneCommand { SceneName = "town", RawElements = new List<object> { el } }, ctx);
        ctx.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("town");
        ctx.State.Get<string>(StateKeys.Screen.ActiveScreen).Should().Be("town");
        ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.Elements).Should().ContainSingle();
        ctx.State.Get<bool>(StateKeys.Scene.Dirty).Should().BeTrue();
    }
}
