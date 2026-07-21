using System.Runtime.CompilerServices;
using System.Threading;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;

namespace LingFanEngine.Tests.Fakes;

/// <summary>
/// 最小手写 fake：场景注册表——仅记录注册的已注册场景名。
/// </summary>
public class FakeSceneRegistry : ISceneRegistry
{
    public List<string> Registered { get; } = new();

    private readonly Dictionary<string, SceneEntity> _scenes = new();

    public void RegisterScene(string sceneName, SceneEntity scene)
    {
        Registered.Add(sceneName);
        _scenes[sceneName] = scene;
    }

    public void Register(string sceneName, params UIElementEntity[] elements) { }

    public SceneEntity? FindScene(string sceneName) => _scenes.TryGetValue(sceneName, out var s) ? s : null;

    public IEnumerable<string> RegisteredScenes => Registered;

    public bool HasScene(string sceneName) => Registered.Contains(sceneName);
}

/// <summary>
/// 最小手写 fake：命令管道——记录投递的命令，其余方法空实现。
/// </summary>
public class FakeCommandPipeline : ICommandPipeline
{
    public List<ICommand> Sent { get; } = new();

    public int Count => Sent.Count;

    public float TimeScale { get; set; } = 1f;

    public ValueTask SendAsync(ICommand command, CancellationToken ct = default)
    {
        Sent.Add(command);
        return default;
    }

    public async IAsyncEnumerable<ICommand> ReceiveAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public bool TryRead(out ICommand command)
    {
        command = null!;
        return false;
    }

    public void Complete() { }
}

/// <summary>
/// 最小手写 fake：故事注册表——记录 ReloadFile 调用并返回可配置的影响场景列表。
/// </summary>
public class FakeStoryRegistry : IStoryRegistry
{
    public List<string> ReloadedFiles { get; } = new();

    public List<string>? ReloadResult { get; set; } = new();

    public Action? OnReloaded { get; set; }

    public int RegisteredCount => 0;

    public int LoadedCount => 0;

    public void Scan() { }

    public bool LoadScene(string sceneName) => false;

    public (IReadOnlyList<ICommand>? Commands, IReadOnlyDictionary<string, int>? Labels)
        GetCompiledResult(string sceneName) => (null, null);

    public (IReadOnlyList<ICommand>? Commands, IReadOnlyDictionary<string, int>? Labels)
        GetCompiledResultByFile(string filePath) => (null, null);

    public IEnumerable<string> GetAllStoryFiles() => Enumerable.Empty<string>();

    public bool LoadSceneFromFile(string filePath) => false;

    public string? FindFileByLabel(string label) => null;

    public bool EnsureLabelLoaded(string label) => false;

    public void RegisterAllDefines() { }

    public bool CanLoad(string sceneName) => false;

    public List<string> ReloadFile(string filePath)
    {
        ReloadedFiles.Add(filePath);
        OnReloaded?.Invoke();
        return ReloadResult ?? new List<string>();
    }
}

/// <summary>
/// 最小手写 fake：UI 线程调度器——记录投递的 Action。
/// </summary>
public class FakeUIThreadDispatcher : IUIThreadDispatcher
{
    public List<Action> Posted { get; } = new();

    public void Post(Action action, bool highPriority = false) => Posted.Add(action);
}
