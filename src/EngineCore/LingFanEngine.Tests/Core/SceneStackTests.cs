using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

public class SceneStackTests
{
    [Fact]
    public void Push_IncrementsCount()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        stack.Push("A");
        stack.Count.Should().Be(1);
        stack.Snapshot[0].SceneName.Should().Be("A");
    }

    [Fact]
    public void Push_SameSceneTwice_DoesNotDuplicate()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        stack.Push("A");
        stack.Push("A");
        stack.Count.Should().Be(1);
    }

    [Fact]
    public void Clear_EmptiesStacks()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        stack.Push("A");
        stack.Clear();
        stack.Count.Should().Be(0);
        stack.ForwardCount.Should().Be(0);
    }

    [Fact]
    public void Back_RestoresCapturedState()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        state.Set("hp", 100);
        stack.Push("A");
        state.Set("hp", 200);
        stack.Push("B");
        state.Set(StateKeys.Scene.CurrentName, "B");

        var snapped = stack.Back();
        snapped.Should().NotBeNull();
        snapped!.SceneName.Should().Be("B");
        state.Get<int>("hp").Should().Be(200);
        stack.Count.Should().Be(1);
    }

    [Fact]
    public void Back_WhenEmpty_ReturnsNull()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        stack.Back().Should().BeNull();
    }

    [Fact]
    public void Forward_RestoresForwardedState()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        state.Set("hp", 100);
        stack.Push("A");
        state.Set("hp", 200);
        stack.Push("B");
        state.Set(StateKeys.Scene.CurrentName, "B");
        stack.Back();
        state.Set(StateKeys.Scene.CurrentName, "A");

        var fwd = stack.Forward();
        fwd.Should().NotBeNull();
        fwd!.SceneName.Should().Be("B");
        stack.ForwardCount.Should().Be(0);
    }

    [Fact]
    public void Forward_WhenEmpty_ReturnsNull()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        stack.Forward().Should().BeNull();
    }

    [Fact]
    public void Peek_ReturnsTopWithoutPopping()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        stack.Push("A");
        stack.Push("B");
        stack.Peek()!.SceneName.Should().Be("B");
        stack.Count.Should().Be(2);
    }

    [Fact]
    public void Trim_RespectsMaxDepth()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state) { MaxDepth = 3 };
        for (var i = 0; i < 6; i++)
            stack.Push($"S{i}");
        stack.Count.Should().Be(3);
        stack.Snapshot[0].SceneName.Should().Be("S3");
    }

    [Fact]
    public void Restore_ReplacesStack()
    {
        var state = new StateContainer();
        var stack = new SceneStack(state);
        var list = new List<SceneSnapshot>
        {
            new() { SceneName = "X" },
            new() { SceneName = "Y" }
        };
        stack.Restore(list);
        stack.Count.Should().Be(2);
        stack.Snapshot[1].SceneName.Should().Be("Y");
        stack.ForwardCount.Should().Be(0);
    }
}
