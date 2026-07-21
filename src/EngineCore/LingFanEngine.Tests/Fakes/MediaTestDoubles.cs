using System.Collections.Generic;
using System.Threading.Tasks;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Abstractions.Scripting;

namespace LingFanEngine.Tests.Fakes;

/// <summary>
/// 记录调用的假音频管理器，用于断言音频类 handler 是否正确调用了播放/停止。
/// </summary>
public sealed class FakeAudioManager : IAudioManager
{
    public float MasterVolume { get; set; }
    public bool MasterMuted { get; set; }
    public float BgmVolume { get; set; }
    public float SeVolume { get; set; }
    public float VoiceVolume { get; set; }

    public string? LastBgmPath;
    public float LastBgmVolume;
    public string? LastSePath;
    public float LastSeVolume;
    public string? LastVoicePath;
    public float LastVoiceVolume;
    public string? LastQueuedBgmPath;
    public string? LastAmbientPath;
    public bool LastAmbientLoop;
    public float LastAmbientVolume;
    public bool StopAmbientCalled;
    public bool StopVoiceCalled;
    public int PlayBgmCount;
    public int PlaySeCount;
    public int PlayVoiceCount;

    public void PlayBgm(string filePath, float volume = 0.8f, bool loop = true)
    {
        LastBgmPath = filePath; LastBgmVolume = volume; PlayBgmCount++;
    }
    public Task PlayBgmAsync(string filePath, float volume = 0.8f, bool loop = true) => Task.CompletedTask;
    public Task QueueBgmAsync(string path, float volume, double crossFadeDuration)
    {
        LastQueuedBgmPath = path; return Task.CompletedTask;
    }
    public Task StopBgmAsync() => Task.CompletedTask;
    public Task StopSeAsync() => Task.CompletedTask;
    public void PlaySe(string filePath, float volume = 1.0f)
    {
        LastSePath = filePath; LastSeVolume = volume; PlaySeCount++;
    }
    public void PlayAmbient(string filePath, float volume = 0.8f, bool loop = true)
    {
        LastAmbientPath = filePath; LastAmbientLoop = loop; LastAmbientVolume = volume;
    }
    public Task StopAmbientAsync() { StopAmbientCalled = true; return Task.CompletedTask; }
    public void PlayVoice(string filePath, float volume = 1.0f)
    {
        LastVoicePath = filePath; LastVoiceVolume = volume; PlayVoiceCount++;
    }
    public void StopVoice() { StopVoiceCalled = true; }
    public Task PauseAllAsync() => Task.CompletedTask;
    public Task ResumeAllAsync() => Task.CompletedTask;
    public Task StopAllAsync() => Task.CompletedTask;
    public void Dispose() { }
}

/// <summary>
/// 记录调用的假视频管理器。
/// </summary>
public sealed class FakeVideoManager : IVideoManager
{
    public float Volume { get; set; }
    public bool IsFinished { get; }
    public event Action? OnFinished;

    public string? LastPlayPath;
    public float LastPlayVolume;
    public bool LastPlayLoop;
    public bool LastPlayAutoPlay;
    public bool StopCalled;
    public bool PauseCalled;
    public bool ResumeCalled;
    public TimeSpan? LastSeek;
    public string? LastCutscenePath;
    public bool LastCutsceneSkipable;
    public float LastCutsceneVolume;
    public int PlayCount;

    public void Play(string path, float volume = 1.0f, bool loop = false, bool autoPlay = true)
    {
        LastPlayPath = path; LastPlayVolume = volume; LastPlayLoop = loop; LastPlayAutoPlay = autoPlay; PlayCount++;
    }
    public void Stop() { StopCalled = true; }
    public void Pause() { PauseCalled = true; }
    public void Resume() { ResumeCalled = true; }
    public void Seek(TimeSpan position) { LastSeek = position; }
    public void PlayCutscene(string path, bool skipable = true, float volume = 1.0f)
    {
        LastCutscenePath = path; LastCutsceneSkipable = skipable; LastCutsceneVolume = volume;
    }
}

/// <summary>
/// 轻量假事件调度器，记录注册/注销/恢复调用。
/// </summary>
public sealed class FakeEventScheduler : IEventScheduler
{
    public List<TimeEventEntity> Entities { get; } = new();
    public List<TimeEventRegistration> Registrations { get; } = new();
    public List<string> Unregistered { get; } = new();
    public List<string> Restored { get; } = new();
    public HashSet<string> Blocked { get; } = new();
    public int ClearEventsCount { get; private set; }

    public IReadOnlyList<TimeEventEntity> GetRegisteredEvents() => Entities;
    public TimeEventSaveState GetSaveState() => new();

    public bool RegisterEvent(TimeEventRegistration registration)
    {
        Registrations.Add(registration); return true;
    }
    public bool UnregisterEvent(string id) => UnregisterEvent(id, UnregisterMode.Normal);
    public bool UnregisterEvent(string id, UnregisterMode mode)
    {
        Unregistered.Add($"{id}:{mode}");
        if (mode == UnregisterMode.Permanent) Blocked.Add(id);
        return true;
    }
    public bool RestoreEvent(string id) { Restored.Add(id); return true; }
    public bool IsBlocked(string id) => Blocked.Contains(id);
    public void RegisterEvent(TimeEventEntity evt) => Entities.Add(evt);
    public void RegisterEvents(IEnumerable<TimeEventEntity> events) => Entities.AddRange(events);
    public void ClearEvents() { ClearEventsCount++; }
    public bool TryDequeuePendingEvent(out TimeEventRegistration? evt) { evt = null; return false; }
    public void MarkFired(string eventId) { }
    public int EventCount => Entities.Count + Registrations.Count;
    public void ApplySaveState(TimeEventSaveState? state) { }
    public void Dispose() { }
}

/// <summary>
/// 假时间事件注册表，支持 TryGetRegistration / GetAllDeclarations。
/// </summary>
public sealed class FakeTimeEventRegistry : ITimeEventRegistry
{
    private readonly Dictionary<string, TimeEventRegistration> _regs = new();
    public bool IsLoaded { get; set; } = true;

    public void Seed(string id, TimeEventRegistration reg) => _regs[id] = reg;

    public bool TryGetRegistration(string id, out TimeEventRegistration registration)
        => _regs.TryGetValue(id, out registration!);
    public IReadOnlyCollection<TimeEventRegistration> GetAllDeclarations() => _regs.Values;
    public IReadOnlyCollection<string> GetAllIds() => _regs.Keys;
    public Task InitializeAsync(IStoryRegistry storyRegistry, IScriptEngine scriptEngine, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    public void RegisterDeclaration(TimeEventRegistration registration) => _regs[registration.Id] = registration;
}

/// <summary>
/// 轻量假场景堆栈，记录 push/back/forward/clear。
/// </summary>
public sealed class FakeSceneStack : ISceneStack
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();

    public int MaxDepth { get; set; } = 100;
    public int Count => _back.Count;
    public int ForwardCount => _forward.Count;
    public IReadOnlyList<SceneSnapshot> Snapshot => _back.Select(s => new SceneSnapshot { SceneName = s }).ToList();

    public void Push(string sceneName)
    {
        _back.Push(sceneName);
        _forward.Clear();
    }
    public SceneSnapshot? Back()
    {
        if (_back.Count == 0) return null;
        var name = _back.Pop();
        _forward.Push(name);
        return new SceneSnapshot { SceneName = name };
    }
    public SceneSnapshot? Forward()
    {
        if (_forward.Count == 0) return null;
        var name = _forward.Pop();
        _back.Push(name);
        return new SceneSnapshot { SceneName = name };
    }
    public SceneSnapshot? Peek() => _back.Count == 0 ? null : new SceneSnapshot { SceneName = _back.Peek() };
    public void Clear() { _back.Clear(); _forward.Clear(); }
    public void Restore(IReadOnlyList<SceneSnapshot> snapshot)
    {
        _back.Clear(); _forward.Clear();
        foreach (var s in snapshot) _back.Push(s.SceneName);
    }
}

/// <summary>
/// 记录调用的假 DSL 执行器，用于回溯/导航类 handler 断言。
/// </summary>
public sealed class FakeDslExecutor : IDslExecutor
{
    public bool CanRollbackResult = true;
    public bool CanRollforwardResult = true;
    public bool IsRunning { get; set; }
    public Func<string, Task>? OnCSharpSceneReplay { get; set; }
    public int RollbackCount;
    public int RollforwardCount;
    public int RollbackToCount;
    public int StopCount;
    public int ClearCheckpointsCount;
    public int StartFromLabelCount;
    public string? LastLabel;

    public void SetStoryRegistry(IStoryRegistry registry) { }
    public void LoadCommands(IReadOnlyList<ICommand> commands, IReadOnlyDictionary<string, int>? labels = null, bool preserveCheckpoints = false) { }
    public void Start() { }
    public void StartFromLabel(string label) { LastLabel = label; StartFromLabelCount++; }
    public void Stop() { StopCount++; }
    public bool CanRollback() => CanRollbackResult;
    public bool CanRollforward() => CanRollforwardResult;
    public bool RollbackTo(int targetPos) { RollbackToCount++; return true; }
    public bool Rollback() { RollbackCount++; return true; }
    public bool Rollforward() { RollforwardCount++; return true; }
    public void ClearCheckpoints() { ClearCheckpointsCount++; }
    public void CreateSceneCheckpoint(string sceneName) { }
}
