using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// GameLoop 集成测试（Phase 3 重组件收尾）。
/// GameLoop 在后台线程跑主循环：每帧从 CommandPipeline 消费命令并经 ICommandDispatcher 分发。
/// 这里用真实 CommandPipeline（Channel）+ SpyCommandDispatcher + 一组 no-op 服务 fake，
/// 验证「启动→消费→分发→停止」的闭环与幂等性，不拉起渲染层。
/// </summary>
public class GameLoopTests
{
    private static GameLoop CreateLoop(
        out SpyCommandDispatcher spy,
        out CommandPipeline pipeline,
        out StateContainer state,
        out FakeStateInitializer initializer)
    {
        state = new StateContainer();
        pipeline = new CommandPipeline();
        spy = new SpyCommandDispatcher();
        var time = new FakeGameTimeService();
        var tween = new FakeTweenEngine();
        var json = new JsonValueConverter();
        initializer = new FakeStateInitializer();
        var anim = new FakeAnimationService();
        var shake = new FakeShakeService();
        var playback = new FakePlaybackService();
        var save = new FakeSaveDataService();
        var handlers = Array.Empty<IDefaultCommandHandler>();

        return new GameLoop(
            pipeline, state, time, spy, tween, json, handlers,
            initializer, anim, shake, playback, save);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (!condition())
        {
            if (cts.IsCancellationRequested)
                throw new TimeoutException("等待 GameLoop 分发命令超时");
            await Task.Delay(5, cts.Token);
        }
    }

    [Fact]
    public async Task StartAsync_RunsLoop_AndDispatchesSentCommand()
    {
        var loop = CreateLoop(out var spy, out var pipeline, out _, out _);

        await loop.StartAsync();
        loop.IsRunning.Should().BeTrue();

        // 经真实管道投递一条命令，主循环应在下一个帧tick消费并分发
        await pipeline.SendAsync(new SetVariableCommand { Key = "k", Value = 42 });

        await WaitUntilAsync(() => spy.Dispatched.Count > 0);

        spy.Dispatched.Count.Should().BeGreaterThan(0);
        spy.Dispatched.Any(c => c is SetVariableCommand).Should().BeTrue();

        await loop.StopAsync();
        loop.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_IsIdempotent_WhenAlreadyRunning()
    {
        var loop = CreateLoop(out _, out _, out _, out _);

        var first = loop.StartAsync();
        var second = loop.StartAsync();

        first.IsCompleted.Should().BeTrue();
        second.IsCompleted.Should().BeTrue();
        loop.IsRunning.Should().BeTrue();

        await loop.StopAsync();
    }

    [Fact]
    public async Task StopAsync_StopsLoop_AndClearsRunning()
    {
        var loop = CreateLoop(out _, out _, out _, out _);

        await loop.StartAsync();
        await loop.StopAsync();

        loop.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WiresStateInitializer_AndDoesNotThrow()
    {
        var loop = CreateLoop(out _, out _, out var state, out var initializer);

        initializer.WasCalled.Should().BeTrue();
        state.Get<bool>("__fake_initializer_called").Should().BeTrue();
        loop.IsRunning.Should().BeFalse();
    }
}
