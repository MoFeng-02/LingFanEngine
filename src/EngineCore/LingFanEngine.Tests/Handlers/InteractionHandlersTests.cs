using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// InteractionHandlers 集成测试：input / wait / hard_pause。
/// </summary>
public class InteractionHandlersTests
{
    [Fact]
    public void InputHandler_WritesPromptAndStoreKey()
    {
        var ctx = new FakeCommandContext();
        new InputHandler().Handle(
            new InputCommand { Prompt = "你的名字？", StoreKey = "player_name", Options = new[] { "张三", "李四" } }, ctx);
        ctx.State.Get<string>(StateKeys.Input.DslPrompt).Should().Be("你的名字？");
        ctx.State.Get<string>(StateKeys.Input.DslStore).Should().Be("player_name");
        ctx.State.Get<string>(StateKeys.Input.DslOptions).Should().Be("张三,李四");
        ctx.State.Get<bool>(StateKeys.Input.DslWaiting).Should().BeTrue();
    }

    [Fact]
    public void WaitHandler_SetsWaitingAndDuration()
    {
        var ctx = new FakeCommandContext();
        new WaitHandler().Handle(new WaitCommand { Seconds = 2.5 }, ctx);
        ctx.State.Get<bool>(StateKeys.Dsl.Waiting).Should().BeTrue();
        ctx.State.Get<double>(StateKeys.Dsl.WaitDuration).Should().Be(2.5);
    }

    [Fact]
    public void HardPauseHandler_ResetsWaitingSayComplete()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Dialog.WaitingSayComplete, true);
        new HardPauseHandler().Handle(new HardPauseCommand(), ctx);
        ctx.State.Get<bool>(StateKeys.Dialog.WaitingSayComplete).Should().BeFalse();
    }
}
