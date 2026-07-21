using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// 最小可配置 ICommandContext 实现，用于纯逻辑 handler 测试。
/// <para>State 返回真实 StateContainer；其余服务默认 null（handler 以 ?. 安全调用）。</para>
/// <para>需要特定行为（存档/场景注册表等）时，设置对应属性或委托。</para>
/// </summary>
public sealed class FakeCommandContext : ICommandContext
{
    private readonly IStateContainer _state;

    public FakeCommandContext(IStateContainer? state = null, LingFanEngineOptions? options = null)
    {
        _state = state ?? new StateContainer();
        Options = options ?? new LingFanEngineOptions();
    }

    public IStateContainer State => _state;
    public LingFanEngineOptions Options { get; }

    // ---- 可注入的服务（默认 null） ----
    public ISaveService? SaveService { get; set; }
    public ISceneRegistry? SceneRegistry { get; set; }
    public IDslExecutor? DslExecutor { get; set; }
    public IStoryRegistry? StoryRegistry { get; set; }
    public II18nService? I18n { get; set; }
    public ISceneStack? SceneStack { get; set; }
    public IEventScheduler? EventScheduler { get; set; }
    public ITimeEventRegistry? TimeEventRegistry { get; set; }
    public IGameTimeService? TimeService { get; set; }

    // 接口要求的其余属性（测试不使用，返回 null；媒体管理器可注入用于断言）
    public ICommandPipeline Pipeline => null!;
    public ITransitionEngine? TransitionEngine => null;
    public IAudioManager? AudioManager { get; set; }
    public IVideoManager? VideoManager { get; set; }
    public Func<byte[]?>? CaptureThumbnail => null;

    // ---- 可覆盖的查询/操作委托 ----
    public Func<string, bool>? ScriptEntryResolver { get; set; }
    public Func<SaveData?>? SaveDataProvider { get; set; }
    public Action<SaveData>? ApplySaveDataAction { get; set; }
    public Action<SaveData, bool>? ApplySaveDataWithFlagAction { get; set; }

    // ---- 调用记录（供断言） ----
    public int ResetInteractionStateCalls { get; private set; }
    public int ClearLocalVariablesCalls { get; private set; }

    public bool TryGetScriptEntry(string sceneName, out SceneScriptEntry? entry)
    {
        if (ScriptEntryResolver != null && ScriptEntryResolver(sceneName))
        {
            entry = new SceneScriptEntry { SceneName = sceneName, SceneType = SceneType.Game, Runner = () => Task.CompletedTask };
            return true;
        }
        entry = null;
        return false;
    }

    public void ResetInteractionState() => ResetInteractionStateCalls++;
    public void ClearLocalVariables() => ClearLocalVariablesCalls++;

    public SaveData? BuildSaveData() => SaveDataProvider?.Invoke();

    public void ApplySaveData(SaveData data) => ApplySaveDataAction?.Invoke(data);
    public void ApplySaveData(SaveData data, bool continueGame) => ApplySaveDataWithFlagAction?.Invoke(data, continueGame);

    public void ReportException(Exception ex, string source) { }
}

/// <summary>
/// 记录调用的假存档服务，用于断言 SaveLoadHandler 是否调用了存档/读档。
/// </summary>
public sealed class FakeSaveService : ISaveService
{
    public string? LastSavedSlot { get; private set; }
    public SaveData? LastSavedData { get; private set; }
    public string? LastLoadedSlot { get; private set; }
    public string? LastDeletedSlot { get; private set; }
    public int SaveCount { get; private set; }
    public int LoadCount { get; private set; }
    public int DeleteCount { get; private set; }

    /// <summary>LoadAsync 返回的存档（null = 不存在）</summary>
    public SaveData? DataToLoad { get; set; }

    public Task<SaveData?> LoadAsync(string slotId)
    {
        LoadCount++;
        LastLoadedSlot = slotId;
        return Task.FromResult(DataToLoad);
    }

    public Task SaveAsync(string slotId, SaveData data)
    {
        SaveCount++;
        LastSavedSlot = slotId;
        LastSavedData = data;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string slotId)
    {
        DeleteCount++;
        LastDeletedSlot = slotId;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<SaveSlotInfo>> GetAllSaveSlotsAsync()
        => Task.FromResult<IEnumerable<SaveSlotInfo>>(Array.Empty<SaveSlotInfo>());

    public Task<bool> ExistsAsync(string slotId) => Task.FromResult(false);
}
