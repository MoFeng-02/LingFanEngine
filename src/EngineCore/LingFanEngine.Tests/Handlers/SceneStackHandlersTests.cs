using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// SceneStackHandlers 集成测试：back / forward / clear_stack 场景级导航。
/// </summary>
public class SceneStackHandlersTests
{
    private static FakeCommandContext WithStack(out FakeSceneStack stack)
    {
        stack = new FakeSceneStack();
        stack.Push("start");
        stack.Push("town");
        return new FakeCommandContext { SceneStack = stack };
    }

    [Fact]
    public void BackHandler_PopsStackAndSetsScene()
    {
        var ctx = WithStack(out var stack);
        new BackHandler().Handle(new BackCommand(), ctx);
        stack.Count.Should().Be(1);
        ctx.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("town");
        ctx.State.Get<bool>(StateKeys.Scene.Dirty).Should().BeTrue();
    }

    [Fact]
    public void BackHandler_EmptyStack_DoesNothing()
    {
        var stack = new FakeSceneStack();
        var ctx = new FakeCommandContext { SceneStack = stack };
        new BackHandler().Handle(new BackCommand(), ctx);
        ctx.State.ContainsKey(StateKeys.Scene.CurrentName).Should().BeFalse();
    }

    [Fact]
    public void ForwardHandler_RestoresScene()
    {
        var ctx = WithStack(out var stack);
        new BackHandler().Handle(new BackCommand(), ctx); // town -> back to start
        new ForwardHandler().Handle(new ForwardCommand(), ctx); // forward to town
        ctx.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("town");
        stack.ForwardCount.Should().Be(0);
    }

    [Fact]
    public void ClearStackHandler_ClearsStack()
    {
        var stack = new FakeSceneStack();
        stack.Push("a");
        stack.Push("b");
        var ctx = new FakeCommandContext { SceneStack = stack };
        new ClearStackHandler().Handle(new ClearStackCommand(), ctx);
        stack.Count.Should().Be(0);
    }
}
