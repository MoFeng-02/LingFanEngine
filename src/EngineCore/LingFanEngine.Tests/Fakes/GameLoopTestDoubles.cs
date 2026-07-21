using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Tests.Fakes;

/// <summary>
/// GameLoop 集成测试所需的轻量 fake / spy 集合。
/// GameLoop 构造函数依赖众多子服务，这里只实现「能跑通主循环」的最小子集，
/// 不引入 Avalonia / LibVLC 等渲染层。所有副作用均为 no-op，仅记录必要调用。
/// </summary>

/// <summary>记录所有被分发的命令——用于验证 GameLoop「消费管道 + 分发」闭环。</summary>
public class SpyCommandDispatcher : ICommandDispatcher
{
    public List<ICommand> Dispatched { get; } = new();

    public void Register<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand { }

    public void Register<TCommand>(Action<TCommand, ICommandContext> handler) where TCommand : ICommand { }

    public void Dispatch(ICommand command, ICommandContext ctx) => Dispatched.Add(command);

    public bool IsRegistered<TCommand>() where TCommand : ICommand => false;
}

/// <summary>游戏时间服务 fake——时间系统默认关闭，不会真的推进时间。</summary>
public class FakeGameTimeService : IGameTimeService
{
    public long TotalMinutes { get; set; }
    public int CurrentDay { get; set; }
    public int CurrentHour { get; set; }
    public int CurrentMinute { get; set; }
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
    public bool IsPaused { get; set; }
    public float TimeScale { get; set; } = 1f;
    public event Action<GameTimeEventArgs>? OnTimeAdvanced;

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;
    public void Tick() { }
    public int SkipTimeMinutes { get; private set; }
    public void SkipTime(int minutes) => SkipTimeMinutes = minutes;
    public void Reset() { }
}

/// <summary>补间引擎 fake。</summary>
public class FakeTweenEngine : ITweenEngine
{
    public int ActiveCount => 0;
    public void AddTween(Tween tween) { }
    public void Update(double deltaTime, float timeScale) { }
    public void Clear() { }
}

/// <summary>状态初始化器 fake——记录是否被调用，并在状态上打标记以便断言。</summary>
public class FakeStateInitializer : IStateInitializer
{
    public bool WasCalled { get; private set; }

    public void Initialize(IStateContainer state)
    {
        WasCalled = true;
        state.Set("__fake_initializer_called", true);
    }
}

/// <summary>控件级动画服务 fake。</summary>
public class FakeAnimationService : IAnimationService
{
    public void Update(double frameDelta, IStateContainer state) { }
}

/// <summary>屏幕震动服务 fake。</summary>
public class FakeShakeService : IShakeService
{
    public void Update(double frameDelta, IStateContainer state) { }
}

/// <summary>跳过/自动播放服务 fake。</summary>
public class FakePlaybackService : IPlaybackService
{
    public void Process(double frameDelta, IStateContainer state) { }
}

/// <summary>存档数据服务 fake——仅满足构造函数契约，不真正持久化。</summary>
public class FakeSaveDataService : ISaveDataService
{
    public SaveData? BuildSaveData() => null;
    public void ApplySaveData(SaveData data) { }
    public void ApplySaveData(SaveData data, bool continueGame) { }
    public void SaveSystemState() { }
    public Task LoadSystemStateAsync() => Task.CompletedTask;
}
