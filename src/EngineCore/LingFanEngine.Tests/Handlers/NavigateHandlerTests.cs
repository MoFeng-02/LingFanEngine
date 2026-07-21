using System.Collections.Generic;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// NavigateHandler 测试：断言导航命令更新场景状态键（CurrentName / ActiveScreen / CurrentType）。
/// <para>使用 FakeSceneRegistry 返回一个 Game 类型场景实体，使处理器进入 navEntity != null 分支。</para>
/// </summary>
public class NavigateHandlerTests
{
    private static FakeCommandContext CreateContextWithScene(string sceneName, SceneType type = SceneType.Game)
    {
        var ctx = new FakeCommandContext();
        ctx.SceneRegistry = new FakeSceneRegistry();
        ctx.SceneRegistry.RegisterScene(sceneName, new SceneEntity
        {
            SceneName = sceneName,
            SceneType = type,
            Elements = []
        });
        return ctx;
    }

    [Fact]
    public void Handle_SceneFound_UpdatesCurrentNameAndActiveScreen()
    {
        var ctx = CreateContextWithScene("town");
        var handler = new NavigateHandler();
        var cmd = new NavigateCommand { Path = "/town" };

        handler.Handle(cmd, ctx);

        ctx.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("town");
        ctx.State.Get<string>(StateKeys.Screen.ActiveScreen).Should().Be("town");
    }

    [Fact]
    public void Handle_SceneFound_SetsCurrentTypeToGame()
    {
        var ctx = CreateContextWithScene("town", SceneType.Game);
        var handler = new NavigateHandler();

        handler.Handle(new NavigateCommand { Path = "/town" }, ctx);

        ctx.State.Get<int>(StateKeys.Scene.CurrentType).Should().Be((int)SceneType.Game);
    }

    [Fact]
    public void Handle_SceneFound_ResetsElementsToEmpty()
    {
        var ctx = CreateContextWithScene("town");
        ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity> { new() { ElementType = "Image" } });
        var handler = new NavigateHandler();

        handler.Handle(new NavigateCommand { Path = "/town" }, ctx);

        var elements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        elements.Should().NotBeNull();
        elements!.Should().BeEmpty();
    }

    [Fact]
    public void Handle_SceneNameTakesPrecedenceOverPath()
    {
        var ctx = CreateContextWithScene("village");
        var handler = new NavigateHandler();
        // 同时给 Path 和 SceneName，SceneName 优先
        var cmd = new NavigateCommand { Path = "/ignored", SceneName = "village" };

        handler.Handle(cmd, ctx);

        ctx.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("village");
    }

    [Fact]
    public void Handle_BackTitleAlias_RedirectsToTitleScene()
    {
        // 目标等于 BackTitleAlias，应被重定向到 TitleSceneName
        var ctx = CreateContextWithScene("title_main");
        var handler = new NavigateHandler();
        var cmd = new NavigateCommand { Path = "/back_title" };

        handler.Handle(cmd, ctx);

        ctx.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("title_main");
    }

    [Fact]
    public void Handle_SceneNotFound_StillSetsElementsAndCurrentType()
    {
        // 没有注册任何场景：所有注册表为空 → 走 TryLabelFallback 分支（DslExecutor 为 null 时提前返回）
        var ctx = new FakeCommandContext();
        var handler = new NavigateHandler();
        var cmd = new NavigateCommand { Path = "/unknown" };

        handler.Handle(cmd, ctx);

        // TryLabelFallback 在没有 DslExecutor 时仅重置元素与类型
        var elements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        elements.Should().NotBeNull();
        ctx.State.Get<int>(StateKeys.Scene.CurrentType).Should().Be((int)SceneType.Game);
    }
}
