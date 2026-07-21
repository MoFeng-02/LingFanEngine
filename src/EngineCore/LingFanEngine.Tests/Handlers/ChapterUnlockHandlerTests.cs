using System.Collections.Generic;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// ChapterUnlockHandler 测试：断言章节条目写入已解锁列表，且幂等/更新语义正确。
/// </summary>
public class ChapterUnlockHandlerTests
{
    [Fact]
    public void Handle_Unlock_AddsChapterToUnlockedList()
    {
        var ctx = new FakeCommandContext();
        var handler = new ChapterUnlockHandler();
        var cmd = new ChapterUnlockCommand { Id = "ch1", ChapterName = "第一章", Unlock = true };

        handler.Handle(cmd, ctx);

        var list = ctx.State.Get<List<ChapterEntry>>(StateKeys.Chapters.Unlocked);
        list.Should().NotBeNull();
        list!.Should().HaveCount(1);
        list[0].Id.Should().Be("ch1");
        list[0].Name.Should().Be("第一章");
        list[0].Unlocked.Should().BeTrue();
    }

    [Fact]
    public void Handle_UnlockSameIdTwice_DoesNotDuplicate()
    {
        var ctx = new FakeCommandContext();
        var handler = new ChapterUnlockHandler();

        handler.Handle(new ChapterUnlockCommand { Id = "ch1", Unlock = true }, ctx);
        handler.Handle(new ChapterUnlockCommand { Id = "ch1", Unlock = true }, ctx);

        var list = ctx.State.Get<List<ChapterEntry>>(StateKeys.Chapters.Unlocked);
        list.Should().HaveCount(1);
    }

    [Fact]
    public void Handle_LockExistingChapter_UpdatesUnlockedFlag()
    {
        var ctx = new FakeCommandContext();
        var handler = new ChapterUnlockHandler();
        handler.Handle(new ChapterUnlockCommand { Id = "ch1", ChapterName = "第一章", Unlock = true }, ctx);
        handler.Handle(new ChapterUnlockCommand { Id = "ch1", Unlock = false }, ctx);

        var list = ctx.State.Get<List<ChapterEntry>>(StateKeys.Chapters.Unlocked);
        list.Should().HaveCount(1);
        list![0].Unlocked.Should().BeFalse();
    }

    [Fact]
    public void Handle_UnlocksMultipleDistinctChapters()
    {
        var ctx = new FakeCommandContext();
        var handler = new ChapterUnlockHandler();
        handler.Handle(new ChapterUnlockCommand { Id = "ch1", Unlock = true }, ctx);
        handler.Handle(new ChapterUnlockCommand { Id = "ch2", Unlock = true }, ctx);

        var list = ctx.State.Get<List<ChapterEntry>>(StateKeys.Chapters.Unlocked);
        list.Should().HaveCount(2);
    }
}
