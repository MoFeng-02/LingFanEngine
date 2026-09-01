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

    // ========== NVL：说话者样式（C2）+ 溢出滚动（C4），2026-09 ==========

    [Fact]
    public void Handle_NvlSpeakerColor_WrapsInlineStyle()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        var handler = new ShowDialogHandler();
        var cmd = new ShowDialogCommand { Text = "你好", Speaker = "Alice", SpeakerColor = "#FFD700" };

        handler.Handle(cmd, ctx);

        var nvl = ctx.State.Get<string>(StateKeys.Nvl.Text);
        nvl.Should().Contain("{color=#FFD700}{b}Alice{/b}{/color}：你好",
            "NVL 说话者应内联加粗+角色色");
    }

    [Fact]
    public void Handle_NvlSpeakerNoColor_PlainInline()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        var handler = new ShowDialogHandler();

        handler.Handle(new ShowDialogCommand { Text = "你好", Speaker = "Alice" }, ctx);

        ctx.State.Get<string>(StateKeys.Nvl.Text).Should().Be("Alice：你好");
    }

    [Fact]
    public void Handle_NvlOverflow_TrimsOldestAndSetsSkipHint()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        var handler = new ShowDialogHandler();

        // 逐句累积 13 行（超 MaxNvlLines=12）
        for (int i = 1; i <= 13; i++)
            handler.Handle(new ShowDialogCommand { Text = $"第{i}行" }, ctx);

        var nvl = ctx.State.Get<string>(StateKeys.Nvl.Text);
        var lines = nvl.Split('\n');
        lines.Length.Should().Be(12, "超出 12 行后最旧行应滚出");
        lines[0].Should().Be("第2行", "最旧的「第1行」被裁掉");
        lines[^1].Should().Be("第13行");
        var hint = ctx.State.Get<int>(StateKeys.Nvl.SkipHint);
        hint.Should().Be(nvl.Length, "SkipHint=裁剪后保留部分的原文长度（打字机只打字新行）");
    }

    [Fact]
    public void Handle_NvlUnderLimit_NoTrimNoHint()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Nvl.Active, true);
        ctx.State.Set(StateKeys.Nvl.SkipHint, -1);
        var handler = new ShowDialogHandler();

        for (int i = 1; i <= 5; i++)
            handler.Handle(new ShowDialogCommand { Text = $"第{i}行" }, ctx);

        ctx.State.Get<int>(StateKeys.Nvl.SkipHint).Should().Be(-1, "未裁剪不应设置 SkipHint");
        ctx.State.Get<string>(StateKeys.Nvl.Text).Should().Contain("第1行");
    }
}
