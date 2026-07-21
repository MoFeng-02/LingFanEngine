using System.Collections.Generic;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// ShowHideHandler 测试：断言 show/hide/background 命令正确操作运行时元素层与背景状态键。
/// </summary>
public class ShowHideHandlerTests
{
    [Fact]
    public void Handle_ShowImage_AddsRuntimeElement()
    {
        var ctx = new FakeCommandContext();
        var handler = new ShowHideHandler();
        var cmd = new ShowHideCommand { Target = "char.png", IsShow = true };

        handler.Handle(cmd, ctx);

        var rt = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements);
        rt.Should().NotBeNull();
        rt!.Should().HaveCount(1);
        rt[0].ElementType.Should().Be("image");
        rt[0].Properties["source"].Should().Be("char.png");
    }

    [Fact]
    public void Handle_ShowBackground_UpdatesCurrentBackgroundAndOrder()
    {
        var ctx = new FakeCommandContext();
        var handler = new ShowHideHandler();
        var cmd = new ShowHideCommand { Target = "bg.png", IsShow = true, IsBackground = true };

        handler.Handle(cmd, ctx);

        var rt = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements);
        rt!.Should().HaveCount(1);
        rt[0].ElementType.Should().Be("background");
        rt[0].Order.Should().Be(-1000);
        ctx.State.Get<string>(StateKeys.Scene.CurrentBackground).Should().Be("bg.png");
    }

    [Fact]
    public void Handle_HideRemovesMatchingElement()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Scene.RuntimeElements, new List<UIElementEntity>
        {
            new() { ElementType = "image", Properties = new() { ["source"] = "a.png" } },
            new() { ElementType = "image", Properties = new() { ["source"] = "b.png" } }
        });
        var handler = new ShowHideHandler();
        var cmd = new ShowHideCommand { Target = "a.png", IsShow = false };

        handler.Handle(cmd, ctx);

        var rt = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements);
        rt.Should().HaveCount(1);
        rt![0].Properties["source"].Should().Be("b.png");
    }

    [Fact]
    public void Handle_HideByTagRemovesElement()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Scene.RuntimeElements, new List<UIElementEntity>
        {
            new() { ElementType = "image", Properties = new() { ["source"] = "a.png", [StateKeys.UiTags.Tag] = "hero" } }
        });
        var handler = new ShowHideHandler();
        var cmd = new ShowHideCommand { Target = "ignored", IsShow = false, Tag = "hero" };

        handler.Handle(cmd, ctx);

        var rt = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements);
        rt.Should().BeEmpty();
    }

    [Fact]
    public void Handle_ShowBackground_RemovesOldBackground()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Scene.RuntimeElements, new List<UIElementEntity>
        {
            new() { ElementType = "background", Properties = new() { ["source"] = "old.png" } }
        });
        var handler = new ShowHideHandler();
        var cmd = new ShowHideCommand { Target = "new.png", IsShow = true, IsBackground = true };

        handler.Handle(cmd, ctx);

        var rt = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements);
        rt.Should().HaveCount(1);
        rt![0].Properties["source"].Should().Be("new.png");
    }
}
