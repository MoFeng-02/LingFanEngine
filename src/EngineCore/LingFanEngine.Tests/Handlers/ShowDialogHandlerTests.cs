using System.Collections.Generic;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// ShowDialogHandler 测试：断言对话文本/说话者状态键被写入，对话历史被记录。
/// </summary>
public class ShowDialogHandlerTests
{
    [Fact]
    public void Handle_SetsDialogTextAndSpeaker()
    {
        var ctx = new FakeCommandContext();
        var handler = new ShowDialogHandler();
        var cmd = new ShowDialogCommand { Text = "你好，世界", Speaker = "Alice" };

        handler.Handle(cmd, ctx);

        ctx.State.Get<string>(StateKeys.Dialog.Text).Should().Be("你好，世界");
        ctx.State.Get<string>(StateKeys.Dialog.Speaker).Should().Be("Alice");
    }

    [Fact]
    public void Handle_ResetsDialogCompleteFlag()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Dialog.Complete, true);
        var handler = new ShowDialogHandler();

        handler.Handle(new ShowDialogCommand { Text = "x" }, ctx);

        ctx.State.Get<bool>(StateKeys.Dialog.Complete).Should().BeFalse();
    }

    [Fact]
    public void Handle_ReplacesVariablePlaceholders()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set("player_name", "小明");
        var handler = new ShowDialogHandler();
        var cmd = new ShowDialogCommand { Text = "欢迎你，{player_name}！" };

        handler.Handle(cmd, ctx);

        ctx.State.Get<string>(StateKeys.Dialog.Text).Should().Be("欢迎你，小明！");
    }

    [Fact]
    public void Handle_RecordsDialogHistory()
    {
        var ctx = new FakeCommandContext();
        var handler = new ShowDialogHandler();
        handler.Handle(new ShowDialogCommand { Text = "第一句", Speaker = "Bob" }, ctx);

        var history = ctx.State.Get<List<DialogHistoryEntry>>(StateKeys.History.Entries);
        history.Should().NotBeNull();
        history!.Should().HaveCount(1);
        history[0].Text.Should().Be("第一句");
        history[0].Speaker.Should().Be("Bob");
    }

    [Fact]
    public void Handle_AppliesExplicitTypewriterDisabled()
    {
        var ctx = new FakeCommandContext();
        var handler = new ShowDialogHandler();
        var cmd = new ShowDialogCommand { Text = "x", TypewriterEnabled = false };

        handler.Handle(cmd, ctx);

        ctx.State.Get<bool>(StateKeys.Dialog.TypewriterEnabled).Should().BeFalse();
    }

    [Fact]
    public void Handle_DefaultTypewriterEnabledIsTrue()
    {
        var ctx = new FakeCommandContext();
        var handler = new ShowDialogHandler();

        handler.Handle(new ShowDialogCommand { Text = "x" }, ctx);

        ctx.State.Get<bool>(StateKeys.Dialog.TypewriterEnabled).Should().BeTrue();
    }
}
