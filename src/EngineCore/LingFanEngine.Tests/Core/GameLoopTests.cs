using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Scripting;
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

    // ======================================================================
    // TargetFps 钳制：value<=0 → 0（不限帧）；否则 Math.Clamp(value, 15, 600)
    // ======================================================================

    [Theory]
    [InlineData(0, 0)]      // 0 = 不限帧
    [InlineData(-5, 0)]     // 负数 = 不限帧
    [InlineData(10, 15)]    // 低于下限钳到 15
    [InlineData(15, 15)]    // 下限边界
    [InlineData(30, 30)]    // 区间内原样
    [InlineData(600, 600)]  // 上限边界
    [InlineData(1000, 600)] // 超上限钳到 600
    public void TargetFps_ClampsToValidRange(int input, int expected)
    {
        var loop = CreateLoop(out _, out _, out _, out _);

        loop.TargetFps = input;

        loop.TargetFps.Should().Be(expected);
    }

    [Fact]
    public void Tween_ReturnsInjectedTweenEngine()
    {
        var loop = CreateLoopWithEvents(
            out _, out _, out _, enableTimeSystem: false,
            out var tween, out _, out _);

        loop.Tween.Should().BeSameAs(tween);
    }

    // ======================================================================
    // RegisterScriptEntry：声明式时间事件自动注册
    //   1. 无条件纳入 ITimeEventRegistry（跨场景查表）
    //   2. 仅当 EnableTimeSystem=true 时注册到 IEventScheduler
    // ======================================================================

    [Fact]
    public void RegisterScriptEntry_RegistersTimeEvents_ToRegistry_Always()
    {
        var loop = CreateLoopWithEvents(
            out _, out _, out _, enableTimeSystem: false,
            out _, out var scheduler, out var registry);

        var entry = MakeEntryWithEvents("scene_a", "evt_morning", "evt_noon");
        loop.RegisterScriptEntry(entry);

        // 注册表无条件收录两个声明
        registry.GetAllDeclarations().Select(r => r.Id)
            .Should().BeEquivalentTo("evt_morning", "evt_noon");

        // 时间系统关闭 → 不进 EventScheduler
        scheduler.Registrations.Should().BeEmpty();
    }

    [Fact]
    public void RegisterScriptEntry_RegistersTimeEvents_ToScheduler_WhenTimeSystemEnabled()
    {
        var loop = CreateLoopWithEvents(
            out _, out _, out _, enableTimeSystem: true,
            out _, out var scheduler, out var registry);

        var entry = MakeEntryWithEvents("scene_b", "evt_x");
        loop.RegisterScriptEntry(entry);

        registry.GetAllDeclarations().Select(r => r.Id).Should().Contain("evt_x");
        scheduler.Registrations.Select(r => r.Id).Should().BeEquivalentTo("evt_x");
    }

    [Fact]
    public void RegisterScriptEntry_WithNoTimeEvents_DoesNotTouchRegistryOrScheduler()
    {
        var loop = CreateLoopWithEvents(
            out _, out _, out _, enableTimeSystem: true,
            out _, out var scheduler, out var registry);

        var entry = new SceneScriptEntry
        {
            SceneName = "scene_empty",
            Runner = () => Task.CompletedTask,
        };
        loop.RegisterScriptEntry(entry);

        registry.GetAllDeclarations().Should().BeEmpty();
        scheduler.Registrations.Should().BeEmpty();
    }

    // ======================================================================
    // OnFrame 事件订阅/退订：合并到内部 UI 回调，仅验证不抛异常
    // ======================================================================

    [Fact]
    public void OnFrame_SubscribeAndUnsubscribe_DoesNotThrow()
    {
        var loop = CreateLoop(out _, out _, out _, out _);

        Action<double> handler = _ => { };

        Action subscribe = () => loop.OnFrame += handler;
        Action unsubscribe = () => loop.OnFrame -= handler;

        subscribe.Should().NotThrow();
        unsubscribe.Should().NotThrow();
    }

    // ======================================================================
    // Dispose 幂等：重复调用安全
    // ======================================================================

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var loop = CreateLoop(out _, out _, out _, out _);

        Action act = () =>
        {
            loop.Dispose();
            loop.Dispose();
        };

        act.Should().NotThrow();
        loop.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_AfterStart_StopsLoop()
    {
        var loop = CreateLoop(out _, out _, out _, out _);

        await loop.StartAsync();
        loop.IsRunning.Should().BeTrue();

        loop.Dispose();

        loop.IsRunning.Should().BeFalse();
    }

    // ======================================================================
    // 辅助：构造带 EventScheduler / TimeEventRegistry / Options 的 GameLoop
    // ======================================================================

    private static GameLoop CreateLoopWithEvents(
        out CommandPipeline pipeline,
        out StateContainer state,
        out SpyCommandDispatcher spy,
        bool enableTimeSystem,
        out FakeTweenEngine tween,
        out FakeEventScheduler scheduler,
        out FakeTimeEventRegistry registry)
    {
        state = new StateContainer();
        pipeline = new CommandPipeline();
        spy = new SpyCommandDispatcher();
        var time = new FakeGameTimeService();
        tween = new FakeTweenEngine();
        var json = new JsonValueConverter();
        var initializer = new FakeStateInitializer();
        var anim = new FakeAnimationService();
        var shake = new FakeShakeService();
        var playback = new FakePlaybackService();
        var save = new FakeSaveDataService();
        var handlers = Array.Empty<IDefaultCommandHandler>();
        scheduler = new FakeEventScheduler();
        registry = new FakeTimeEventRegistry();
        var options = new LingFanEngineOptions { EnableTimeSystem = enableTimeSystem };

        return new GameLoop(
            pipeline, state, time, spy, tween, json, handlers,
            initializer, anim, shake, playback, save,
            saveService: null, sceneRegistry: null, options: options,
            eventScheduler: scheduler, timeEventRegistry: registry);
    }

    private static SceneScriptEntry MakeEntryWithEvents(string sceneName, params string[] eventIds)
    {
        var events = eventIds
            .Select(id => new TimeEventRegistration { Id = id, Hour = 8 })
            .ToList();

        return new SceneScriptEntry
        {
            SceneName = sceneName,
            Runner = () => Task.CompletedTask,
            TimeEvents = events,
        };
    }
}
