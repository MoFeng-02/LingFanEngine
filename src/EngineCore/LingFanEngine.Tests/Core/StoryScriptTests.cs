using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Games;
using LingFanEngine.Services.Core;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// StoryScript（C# 剧情脚本基类）单元测试。
/// <para>
/// StoryScript 的场景构建方法（SetScene/AddButton/AddMenu/AddImage/AddText）直接写 _state，
/// 剧情流方法（Set/Define/ChoiceAsync…）是对 IGameController 的薄封装。
/// 这里用具体子类 <see cref="TestStory"/> 暴露 protected 方法，
/// 以真实 StateContainer + GameController（EngineTestHost）验证「写状态」与「发命令」两条路径。
/// </para>
/// </summary>
public class StoryScriptTests
{
    /// <summary>具体测试剧本：把需要验证的 protected 成员透传为 public。</summary>
    private sealed class TestStory : StoryScript
    {
        public override string SceneName => "test_scene";
        public override Task RunAsync() => Task.CompletedTask;

        public void CallSetScene(string bg, string? title = null) => SetScene(bg, title);
        public void CallAddButton(string label, double x, double y, double w, double h,
            string? nav = null, string? cmd = null) => AddButton(label, x, y, w, h, nav, cmd);
        public void CallAddMenu(string prompt, params (string, string)[] options) => AddMenu(prompt, options);
        public void CallAddImage(string src, double x, double y) => AddImage(src, x, y);
        public void CallAddText(string text, double x, double y) => AddText(text, x, y);

        public void CallSet(string key, object? value) => Set(key, value);
        public void CallDefine(string key, object? value) => Define(key, value);
        public Task<int> CallChoiceAsync(string prompt, CancellationToken ct, params string[] options)
            => ChoiceAsync(prompt, ct, options);
        public Task CallChoiceBranchAsync(string prompt, CancellationToken ct,
            params (string Label, Func<Task> OnSelect)[] options)
            => ChoiceAsync(prompt, ct, options);
    }

    private static TestStory NewStory(out EngineTestHost host, ICommandPipeline? pipeline = null)
    {
        host = new EngineTestHost(pipeline: pipeline);
        var story = new TestStory();
        story.Initialize(host.GameController, host.State, host.Pipeline, new FakeSceneRegistry());
        return story;
    }

    private static List<UIElementEntity> Elements(EngineTestHost host)
        => host.State.Get<List<UIElementEntity>>(StateKeys.Scene.Elements) ?? new();

    // ========== 场景构建：写 _state ==========

    [Fact]
    public void SetScene_WritesBackgroundElement_AndSceneName_AndDirty()
    {
        var story = NewStory(out var host);

        story.CallSetScene("bg/room.png");

        var elems = Elements(host);
        elems.Should().HaveCount(1);
        elems[0].ElementType.Should().Be("background");
        elems[0].Properties["source"].Should().Be("bg/room.png");
        host.State.Get<string>(StateKeys.Scene.CurrentName).Should().Be("test_scene");
        host.State.Get<bool>(StateKeys.Scene.Dirty).Should().BeTrue();
    }

    [Fact]
    public void SetScene_WithTitle_AddsTitleTextElement()
    {
        var story = NewStory(out var host);

        story.CallSetScene("bg/room.png", "第一章");

        var elems = Elements(host);
        elems.Should().HaveCount(2);
        elems[1].ElementType.Should().Be("text");
        elems[1].Properties["text"].Should().Be("第一章");
    }

    [Fact]
    public void SetScene_ClearsPreviousElements()
    {
        var story = NewStory(out var host);

        story.CallAddText("stale", 0, 0);
        Elements(host).Should().NotBeEmpty();

        story.CallSetScene("bg/new.png");

        // SetScene 用新列表整体替换，旧元素被清空
        var elems = Elements(host);
        elems.Should().HaveCount(1);
        elems[0].Properties["source"].Should().Be("bg/new.png");
    }

    [Fact]
    public void AddButton_AppendsButtonElement_WithNavAndCmd()
    {
        var story = NewStory(out var host);

        story.CallAddButton("去森林", 10, 20, 100, 40, nav: "forest");
        story.CallAddButton("战斗", 10, 70, 100, 40, cmd: "do_fight");

        var elems = Elements(host);
        elems.Should().HaveCount(2);
        elems.Should().OnlyContain(e => e.ElementType == "button");
        elems[0].Properties["nav"].Should().Be("forest");
        elems[1].Properties["cmd"].Should().Be("do_fight");
        host.State.Get<bool>(StateKeys.Scene.Dirty).Should().BeTrue();
    }

    [Fact]
    public void AddMenu_AddsPromptAndButtons_AndResetsDialogKeys()
    {
        var story = NewStory(out var host);

        story.CallAddMenu("选择路线", ("上山", "mountain"), ("下海", "do_dive"));

        var elems = Elements(host);
        // 1 个提示文本 + 2 个按钮
        elems.Should().HaveCount(3);
        elems[0].ElementType.Should().Be("text");
        elems[0].Properties["text"].Should().Be("选择路线");
        elems.Skip(1).Should().OnlyContain(e => e.ElementType == "button");
        // 第一项非 do_ 前缀 → 走导航；第二项 do_ 前缀 → 走命令
        elems[1].Properties.Should().ContainKey("nav");
        elems[2].Properties.Should().ContainKey("cmd");

        host.State.Get<string>(StateKeys.Dialog.Text).Should().Be("");
        host.State.Get<bool>(StateKeys.Dialog.Complete).Should().BeFalse();
        host.State.Get<bool>(StateKeys.Dialog.WaitingSayComplete).Should().BeFalse();
    }

    [Fact]
    public void AddImage_And_AddText_AppendCorrectElementTypes()
    {
        var story = NewStory(out var host);

        story.CallAddImage("cg/hero.png", 100, 100);
        story.CallAddText("你好", 20, 20);

        var elems = Elements(host);
        elems.Should().HaveCount(2);
        elems[0].ElementType.Should().Be("image");
        elems[0].Properties["source"].Should().Be("cg/hero.png");
        elems[1].ElementType.Should().Be("text");
        elems[1].Properties["text"].Should().Be("你好");
    }

    // ========== 剧情流：发命令 / 读状态 ==========

    [Fact]
    public void Set_And_Define_SendSetVariableCommand()
    {
        var fake = new FakeCommandPipeline();
        var story = NewStory(out _, pipeline: fake);

        story.CallSet("gold", 100);
        story.CallDefine("hp", 50);

        var sets = fake.Sent.OfType<SetVariableCommand>().ToList();
        sets.Should().HaveCount(2);
        sets[0].Key.Should().Be("gold");
        sets[0].Value.Should().Be(100);
        sets[0].IsDefine.Should().BeFalse();
        sets[1].Key.Should().Be("hp");
        sets[1].IsDefine.Should().BeTrue();
    }

    [Fact]
    public async Task ChoiceAsync_ReturnsSelectedIndex()
    {
        var story = NewStory(out var host);

        var task = story.CallChoiceAsync("选择", CancellationToken.None, "A", "B", "C");
        await Task.Yield();
        host.SelectMenu(1);

        var idx = await task.WaitAsync(TimeSpan.FromSeconds(5));
        idx.Should().Be(1);
    }

    [Fact]
    public async Task ChoiceAsync_Branch_InvokesSelectedCallback()
    {
        var story = NewStory(out var host);

        var picked = -1;
        var task = story.CallChoiceBranchAsync("选择", CancellationToken.None,
            ("第一", () => { picked = 0; return Task.CompletedTask; }),
            ("第二", () => { picked = 1; return Task.CompletedTask; }));

        await Task.Yield();
        host.SelectMenu(1);

        await task.WaitAsync(TimeSpan.FromSeconds(5));
        picked.Should().Be(1);
    }
}
