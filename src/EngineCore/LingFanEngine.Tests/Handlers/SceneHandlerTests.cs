using System.Collections.Generic;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// SceneHandler 测试：断言 scene 命令切换场景状态键（CurrentName / ActiveScreen / CurrentType / Elements）。
/// 未提供 SceneRegistry 时走降级分支，仍设置场景名与空元素列表。
/// </summary>
public class SceneHandlerTests
{
    [Fact]
    public void Handle_UnknownScene_SetsCurrentNameAndActiveScreen()
    {
        var ctx = new FakeCommandContext();
        var handler = new SceneHandler();
        var cmd = new SceneCommand { SceneName = "town" };

        handler.Handle(cmd, ctx);

        ctx.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("town");
        ctx.State.Get<string>(StateKeys.Screen.ActiveScreen).Should().Be("town");
    }

    [Fact]
    public void Handle_SetsCurrentTypeToGame()
    {
        var ctx = new FakeCommandContext();
        var handler = new SceneHandler();

        handler.Handle(new SceneCommand { SceneName = "any" }, ctx);

        ctx.State.Get<int>(StateKeys.Scene.CurrentType).Should().Be((int)SceneType.Game);
    }

    [Fact]
    public void Handle_InitializesEmptyElements()
    {
        var ctx = new FakeCommandContext();
        var handler = new SceneHandler();

        handler.Handle(new SceneCommand { SceneName = "any" }, ctx);

        var elements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        elements.Should().NotBeNull();
        elements!.Should().BeEmpty();
    }

    [Fact]
    public void Handle_KnownScene_SetsCurrentName()
    {
        var ctx = new FakeCommandContext();
        ctx.SceneRegistry = new FakeSceneRegistry();
        ctx.SceneRegistry.RegisterScene("village", new SceneEntity
        {
            SceneName = "village",
            SceneType = SceneType.Game,
            Elements = []
        });
        var handler = new SceneHandler();

        handler.Handle(new SceneCommand { SceneName = "village" }, ctx);

        ctx.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("village");
        ctx.State.Get<int>(StateKeys.Scene.CurrentType).Should().Be((int)SceneType.Game);
    }

    [Fact]
    public void Handle_ClearsMenuReturnMarker()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Scene.MenuReturnTo, "old");
        var handler = new SceneHandler();

        handler.Handle(new SceneCommand { SceneName = "x" }, ctx);

        ctx.State.Get<string>(StateKeys.Scene.MenuReturnTo).Should().BeNull();
    }
}
