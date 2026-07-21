using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// DSL 2.0 命令处理器集成测试：数组/字典、立绘、背景切换、Live2D、成就、图鉴、存档删除。
/// </summary>
public class Dsl2HandlersTests
{
    // ---- 数组 / 字典 ----

    [Fact]
    public void ArrayPushHandler_AppendsToList()
    {
        var ctx = new FakeCommandContext();
        new ArrayPushHandler().Handle(new ArrayPushCommand { Key = "inv", ValuePart = "\"sword\"" }, ctx);
        new ArrayPushHandler().Handle(new ArrayPushCommand { Key = "inv", ValuePart = "\"shield\"" }, ctx);
        var list = ctx.State.Get<List<object?>>("inv");
        list.Should().HaveCount(2);
        list[0].Should().Be("sword");
        list[1].Should().Be("shield");
    }

    [Fact]
    public void ArrayPopHandler_RemovesLastAndStoresPopped()
    {
        var ctx = new FakeCommandContext();
        new ArrayPushHandler().Handle(new ArrayPushCommand { Key = "inv", ValuePart = "\"a\"" }, ctx);
        new ArrayPushHandler().Handle(new ArrayPushCommand { Key = "inv", ValuePart = "\"b\"" }, ctx);
        new ArrayPopHandler().Handle(new ArrayPopCommand { Key = "inv" }, ctx);
        ctx.State.Get<List<object?>>("inv").Should().HaveCount(1);
        ctx.State.Get<object?>("inv_popped").Should().Be("b");
    }

    [Fact]
    public void DictSetHandler_SetsField()
    {
        var ctx = new FakeCommandContext();
        new DictSetHandler().Handle(new DictSetCommand { Key = "player", Field = "name", ValuePart = "\"bob\"" }, ctx);
        var dict = ctx.State.Get<Dictionary<string, object?>>("player");
        dict!["name"].Should().Be("bob");
    }

    // ---- 立绘 ----

    [Fact]
    public void SpriteHandler_ShowAddsElement_And_HideRemoves()
    {
        var ctx = new FakeCommandContext();
        new SpriteHandler().Handle(new SpriteCommand { Operation = "show", Id = "alice", Source = "a.png" }, ctx);
        var list = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements);
        list.Any(e => e.ElementType == "image" &&
            e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) && t?.ToString() == "alice")
            .Should().BeTrue();

        new SpriteHandler().Handle(new SpriteCommand { Operation = "hide", Id = "alice" }, ctx);
        ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements)
            .Any(e => e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) && t?.ToString() == "alice")
            .Should().BeFalse();
    }

    [Fact]
    public void SpriteHandler_StateChangesSource()
    {
        var ctx = new FakeCommandContext();
        new SpriteHandler().Handle(new SpriteCommand { Operation = "show", Id = "alice", Source = "a1.png" }, ctx);
        new SpriteHandler().Handle(new SpriteCommand { Operation = "state", Id = "alice", Emotion = "a2.png" }, ctx);
        var el = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements)
            .Find(e => e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) && t?.ToString() == "alice");
        el!.Properties["source"].Should().Be("a2.png");
    }

    // ---- 背景切换 ----

    [Fact]
    public void BgSwitchHandler_WithoutTransition_SetsBackgroundOnly()
    {
        var ctx = new FakeCommandContext();
        new BgSwitchHandler().Handle(new BgSwitchCommand { Path = "bg2.png" }, ctx);
        ctx.State.Get<bool>(StateKeys.Transition.Active).Should().BeFalse();
        ctx.State.Get<string>(StateKeys.Scene.CurrentBackground).Should().Be("bg2.png");
    }

    [Fact]
    public void BgSwitchHandler_WithTransition_ActivatesTransition()
    {
        var ctx = new FakeCommandContext();
        new BgSwitchHandler().Handle(new BgSwitchCommand { Path = "bg3.png", Transition = "fade", Duration = 0.5 }, ctx);
        ctx.State.Get<bool>(StateKeys.Transition.Active).Should().BeTrue();
        ctx.State.Get<string>(StateKeys.Transition.Type).Should().Be("fade");
        ctx.State.Get<string>(StateKeys.Scene.CurrentBackground).Should().Be("bg3.png");
    }

    // ---- Live2D ----

    [Fact]
    public void Live2DHandler_CharRegistersConfig()
    {
        var ctx = new FakeCommandContext();
        var cfg = new Dictionary<string, object?> { ["model"] = "m" };
        new Live2DHandler().Handle(new Live2DCommand { Operation = "char", Id = "m", Config = cfg }, ctx);
        ctx.State.Get<Dictionary<string, object?>>(StateKeys.Live2D.CharPrefix + "m").Should().NotBeNull();
    }

    [Fact]
    public void Live2DHandler_ShowAddsElement_And_PauseSetsFlag()
    {
        var ctx = new FakeCommandContext();
        new Live2DHandler().Handle(new Live2DCommand { Operation = "show", Id = "m" }, ctx);
        ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements)
            .Should().ContainSingle(e => e.ElementType == "Live2D");

        new Live2DHandler().Handle(new Live2DCommand { Operation = "pause", Id = "m" }, ctx);
        ctx.State.Get<bool>(StateKeys.Live2D.ModelPrefix + "m" + StateKeys.Live2D.PausedSuffix).Should().BeTrue();
        new Live2DHandler().Handle(new Live2DCommand { Operation = "resume", Id = "m" }, ctx);
        ctx.State.Get<bool>(StateKeys.Live2D.ModelPrefix + "m" + StateKeys.Live2D.PausedSuffix).Should().BeFalse();
    }

    // ---- 成就 / 图鉴 ----

    [Fact]
    public void AchievementUnlockHandler_AddsAndIsIdempotent()
    {
        var ctx = new FakeCommandContext();
        new AchievementUnlockHandler().Handle(new AchievementUnlockCommand { Id = "a1", AchievementName = "首胜" }, ctx);
        new AchievementUnlockHandler().Handle(new AchievementUnlockCommand { Id = "a1", AchievementName = "首胜" }, ctx);
        ctx.State.Get<List<AchievementEntry>>(StateKeys.Achievements.Unlocked).Should().ContainSingle(e => e.Id == "a1");
    }

    [Fact]
    public void UnlockGalleryHandler_AddsEntry()
    {
        var ctx = new FakeCommandContext();
        new UnlockGalleryHandler().Handle(new UnlockGalleryCommand { Id = "cg1", ImagePath = "x.png", Title = "CG" }, ctx);
        ctx.State.Get<List<GalleryEntry>>(StateKeys.Gallery.Unlocked).Should().ContainSingle(e => e.Id == "cg1");
    }

    // ---- 存档删除 ----

    [Fact]
    public void SaveDeleteHandler_NullSaveService_DoesNotThrow()
    {
        // 仅验证 null 安全：SaveService 为 null 时 Handler 静默返回，不应抛异常。
        // 真实删除路径见下方 WithSaveService 正例测试。
        var ctx = new FakeCommandContext(); // SaveService 默认 null
        var act = () => new SaveDeleteHandler().Handle(new SaveDeleteCommand { SlotId = "slot1" }, ctx);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SaveDeleteHandler_WithSaveService_InvokesDeleteAsyncWithSlot()
    {
        // 正例：注入 FakeSaveService，断言删除存档命令确实调用了 DeleteAsync 且槽位正确。
        var save = new FakeSaveService();
        var ctx = new FakeCommandContext { SaveService = save };

        new SaveDeleteHandler().Handle(new SaveDeleteCommand { SlotId = "slotA" }, ctx);

        // 真实删除经由 Task.Run 异步执行，轮询等待其完成（带超时保护，避免偶发竞态导致假失败）。
        for (var i = 0; i < 50 && save.DeleteCount == 0; i++)
            await Task.Delay(10);

        save.DeleteCount.Should().Be(1);
        save.LastDeletedSlot.Should().Be("slotA");
    }
}
