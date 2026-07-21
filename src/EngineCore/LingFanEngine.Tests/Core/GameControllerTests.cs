using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// GameController 富阻塞交互方法集成测试（Phase 3）。
/// 验证「发命令 + 等状态键」闭环在无 Avalonia 渲染宿主下可正常推进、不挂死。
/// 每个 await 都用 WaitAsync(超时) 兜底，确保即使断言写错也不会让整个测试运行挂起。
/// </summary>
public class GameControllerTests
{
    [Fact]
    public async Task SayAsync_Completes_WhenDialogAdvanced()
    {
        // 排雷最小闭环：SayAsync 先发 ShowDialogCommand，再等 WaitingSayComplete 变 true。
        var host = new EngineTestHost();
        var sayTask = host.GameController.SayAsync("你好，世界", "旁白");
        await Task.Yield();                       // 让 SayAsync 注册 waiter 并置 false
        host.AdvanceDialog();                     // 模拟玩家点击推进
        await sayTask.WaitAsync(TimeSpan.FromSeconds(5));

        sayTask.IsCompletedSuccessfully.Should().BeTrue();
        host.State.Get<bool>(StateKeys.Dialog.WaitingSayComplete).Should().BeFalse();
    }

    [Fact]
    public async Task SayAsync_SendsShowDialogCommand_WithTextAndSpeaker()
    {
        var fake = new FakeCommandPipeline();
        var host = new EngineTestHost(pipeline: fake);

        var sayTask = host.GameController.SayAsync("第一句", "爱丽丝", typewriter: false);
        await Task.Yield();
        host.AdvanceDialog();
        await sayTask.WaitAsync(TimeSpan.FromSeconds(5));

        var sent = fake.Sent.OfType<ShowDialogCommand>().ToList();
        sent.Should().ContainSingle();
        sent[0].Text.Should().Be("第一句");
        sent[0].Speaker.Should().Be("爱丽丝");
        sent[0].TypewriterEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ShowMenuAsync_ReturnsSelectedIndex()
    {
        var host = new EngineTestHost();

        var menuTask = host.GameController.ShowMenuAsync("选择一项", new[] { "A", "B", "C" });
        await Task.Yield();
        host.SelectMenu(1);                       // 玩家选第 2 项
        var selected = await menuTask.WaitAsync(TimeSpan.FromSeconds(5));

        selected.Should().Be(1);
    }

    [Fact]
    public async Task InputAsync_ReturnsSubmittedResult()
    {
        var host = new EngineTestHost();

        var inputTask = host.GameController.InputAsync("请输入名字");
        await Task.Yield();
        host.SetInput("小明");
        var result = await inputTask.WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().Be("小明");
    }

    [Fact]
    public async Task TransitionAsync_Completes_WhenTransitionDeactivated()
    {
        var fake = new FakeCommandPipeline();
        var host = new EngineTestHost(pipeline: fake);

        var transTask = host.GameController.TransitionAsync("FadeIn", 0.3);
        await Task.Yield();
        host.CompleteTransition();                // 先激活再结束
        await transTask.WaitAsync(TimeSpan.FromSeconds(5));

        transTask.IsCompletedSuccessfully.Should().BeTrue();
        fake.Sent.OfType<TransitionCommand>().Should().ContainSingle(c => c.Type == "FadeIn");
    }

    [Fact]
    public async Task WaitAsync_CompletesAfterDelay()
    {
        var host = new EngineTestHost();
        var start = DateTime.UtcNow;

        await host.GameController.WaitAsync(0.05).WaitAsync(TimeSpan.FromSeconds(5));

        (DateTime.UtcNow - start).Should().BeGreaterThan(TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public async Task WaitForClickAsync_Completes_WhenDialogAdvanced()
    {
        var host = new EngineTestHost();
        var task = host.GameController.WaitForClickAsync();
        await Task.Yield();
        host.AdvanceDialog();
        await task.WaitAsync(TimeSpan.FromSeconds(5));

        task.IsCompletedSuccessfully.Should().BeTrue();
    }
}
