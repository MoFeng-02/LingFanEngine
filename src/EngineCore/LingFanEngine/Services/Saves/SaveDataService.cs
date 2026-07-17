using System.Diagnostics;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Saves;

/// <summary>
/// 存档数据服务实现
/// <para>负责存档数据的构建、恢复和系统偏好持久化。</para>
/// <para>从 GameLoop 提取，解除 GameLoop 对存档逻辑的直接持有。</para>
/// </summary>
public class SaveDataService : ISaveDataService
{
    private readonly IStateContainer _state;
    private readonly IJsonValueConverter _jsonConverter;
    private readonly LingFanEngineOptions _options;
    private readonly ISaveService? _saveService;
    private readonly ISceneStack? _sceneStack;
    private readonly ISceneRegistry? _sceneRegistry;
    private readonly IStoryRegistry? _storyRegistry;
    private readonly IDslExecutor? _dslExecutor;
    private readonly IEventScheduler? _eventScheduler;

    /// <summary>截图回调（由 GameLoop 在 SetSceneView 时设置）</summary>
    public Func<byte[]?>? CaptureThumbnail { get; set; }

    /// <summary>脚本入口查询回调（由 GameLoop 设置，用于 C# StoryScript 场景恢复）</summary>
    public Func<string, SceneScriptEntry?>? TryGetScriptEntry { get; set; }

    /// <summary>重置交互状态回调（由 GameLoop 设置，ApplySaveData 开头调用）</summary>
    public Action? OnResetInteractionState { get; set; }

    /// <summary>
    /// P2-#15: after_load 钩子——读档完成后调用（对标 Ren'Py label after_load）
    /// <para>游戏可注册此回调执行自定义初始化逻辑（如修正变量、重新计算派生状态）。</para>
    /// </summary>
    public Action<SaveData>? OnAfterLoad { get; set; }

    /// <summary>
    /// P1-#13: 存档版本迁移回调
    /// <para>当存档的 GameVersion 与当前引擎版本不匹配时调用。</para>
    /// <para>回调可修改 SaveData 以兼容新版本。返回 false 表示拒绝加载。</para>
    /// </summary>
    public Func<SaveData, string, bool>? OnSaveMigration { get; set; }

    public SaveDataService(
        IStateContainer state,
        IJsonValueConverter jsonConverter,
        LingFanEngineOptions options,
        ISaveService? saveService = null,
        ISceneStack? sceneStack = null,
        ISceneRegistry? sceneRegistry = null,
        IStoryRegistry? storyRegistry = null,
        IDslExecutor? dslExecutor = null,
        IEventScheduler? eventScheduler = null)
    {
        _state = state;
        _jsonConverter = jsonConverter;
        _options = options;
        _saveService = saveService;
        _sceneStack = sceneStack;
        _sceneRegistry = sceneRegistry;
        _storyRegistry = storyRegistry;
        _dslExecutor = dslExecutor;
        _eventScheduler = eventScheduler;
    }

    /// <inheritdoc/>
    public SaveData? BuildSaveData()
    {
        var currentSceneName = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";
        var currentType = (SceneType)_state.Get<int>(StateKeys.Scene.CurrentType);

        // ===== Menu/UI 场景：存档当前游戏状态（对标 Ren'Py Esc 菜单存档） =====
        // 游戏变量仍在 StateContainer 中（Menu 不侵入游戏状态），
        // 但 DSL 执行位置已被 Menu 的 LoadCommands 重置，需从 GameDslIndex 恢复。
        string saveSceneName = currentSceneName;
        int dslCurrentIndex;
        string dslWaitingType;

        if (currentType != SceneType.Game)
        {
            var menuReturnTo = _state.Get<string>(StateKeys.Scene.MenuReturnTo);
            if (string.IsNullOrEmpty(menuReturnTo))
            {
                // 没有正在进行的游戏 → 拒绝存档
                Debug.WriteLine($"[SaveDataService] {currentType} 场景无正在进行的游戏，拒绝存档: {currentSceneName}");
                return null;
            }

            // 使用游戏场景名 + 进入菜单前保存的 DSL 位置
            saveSceneName = menuReturnTo;
            dslCurrentIndex = _state.Get<int>(StateKeys.Scene.GameDslIndex);
            dslWaitingType = _state.Get<string>(StateKeys.Scene.GameDslWaitingType) ?? "";
            Debug.WriteLine($"[SaveDataService] 从 {currentType} 场景[{currentSceneName}]存档游戏状态: {saveSceneName}, dslIndex={dslCurrentIndex}");
        }
        else
        {
            // Game 场景：直接使用当前 DSL 位置
            dslCurrentIndex = _state.Get<int>(StateKeys.Dsl.CurrentIndex);
            dslWaitingType = _state.Get<string>(StateKeys.Dsl.WaitingType) ?? "";
        }

        // 1. 收集全量用户状态（排除 __ 系统变量和场景元素）
        //    场景元素不存档——读档时从 DSL 命令列表回放 ShowElementCommand 重建
        //    这避免了引用共享导致存档中混入错误场景元素的问题
        //    Phase 36: 只写 TypedState（V2 类型安全），State 留空（V1 兼容回退用）
        var typedState = new Dictionary<string, SaveEntry>();
        var allState = new Dictionary<string, object?>(_state.GetSnapshot());

        foreach (var (k, v) in allState)
        {
            if (string.IsNullOrEmpty(k) || k.StartsWith("_local_"))
                continue;
            // 排除场景元素和运行时元素——读档时回放重建
            if (k == StateKeys.Scene.Elements || k == StateKeys.Scene.RuntimeElements)
                continue;
            // 排除 Game* 系列临时键
            if (k == StateKeys.Scene.GameSceneElements || k == StateKeys.Scene.GameRuntimeElements
                || k == StateKeys.Scene.GameCurrentBackground || k == StateKeys.Scene.GameDslIndex
                || k == StateKeys.Scene.GameDslWaitingType)
                continue;
            // 排除其他 __ 系统变量（保留游戏时间、成就、章节、CG鉴赏等玩家进度）
            if (k.StartsWith(StateKeys.SystemPrefix)
                && !(_options.EnableTimeSystem && k.StartsWith(StateKeys.GameTime.Prefix))
                && k != StateKeys.Achievements.Unlocked
                && k != StateKeys.Chapters.Unlocked
                && k != StateKeys.Gallery.Unlocked)
                continue;

            typedState[k] = ToSaveEntry(v);
        }

        // 2. 收集 SceneStack 快照
        var stackSnapshot = _sceneStack?.Snapshot?.ToList() ?? new List<SceneSnapshot>();

        byte[]? thumb = null;
        try { thumb = CaptureThumbnail?.Invoke(); }
        catch (Exception ex) { Debug.WriteLine($"[SaveDataService] CaptureThumbnail failed: {ex.Message}"); }

        var data = new SaveData
        {
            GameVersion = _options.GameVersion,
            Name = _options.SaveNameFormatter?.Invoke(saveSceneName) ?? $"存档 - {saveSceneName}",
            SceneName = saveSceneName,
            // Phase 36: 只写 TypedState，State 保持默认空字典（减少约 40% JSON 体积）
            // 加载时优先 TypedState，为空则回退 State + ConvertJsonValue（V1 兼容）
            TypedState = typedState,
            SceneStackSnapshot = stackSnapshot,
            Thumbnail = thumb,
            DslCurrentIndex = dslCurrentIndex,
            DslWaitingType = dslWaitingType,
            SceneType = SceneType.Game,
        };

        // Phase 60-61: 时间事件持久化（仅小说世界模式）
        if (_options.EnableTimeSystem && _eventScheduler != null)
        {
            data.TimeEvents = _eventScheduler.GetRegisteredEvents().ToList();
            data.TimeEventState = _eventScheduler.GetSaveState();
        }

        return data;
    }

    /// <inheritdoc/>
    public void ApplySaveData(SaveData data) => ApplySaveData(data, continueGame: true);

    /// <inheritdoc/>
    public void ApplySaveData(SaveData data, bool continueGame)
    {
        // P1-#13: 存档版本迁移检查
        if (!string.IsNullOrEmpty(data.GameVersion) && data.GameVersion != _options.GameVersion)
        {
            Debug.WriteLine($"[SaveDataService] 存档版本不匹配: 存档={data.GameVersion}, 当前={_options.GameVersion}");
            if (OnSaveMigration != null)
            {
                var accepted = OnSaveMigration.Invoke(data, _options.GameVersion);
                if (!accepted)
                {
                    Debug.WriteLine("[SaveDataService] 存档迁移被拒绝，取消加载");
                    return;
                }
            }
            // 无迁移回调——继续加载（最佳努力兼容）
        }
        var beforeSceneName = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";
        var beforeType = (SceneType)_state.Get<int>(StateKeys.Scene.CurrentType);
        var beforeElements = _state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
        Debug.WriteLine($"[SaveDataService] ===== ApplySaveData START =====");
        Debug.WriteLine($"[SaveDataService] BEFORE: scene={beforeSceneName}, type={beforeType}, elements.count={beforeElements?.Count ?? 0}, dslIdx={_state.Get<int>(StateKeys.Dsl.CurrentIndex)}");
        Debug.WriteLine($"[SaveDataService] SAVE DATA: scene={data.SceneName}, dslIdx={data.DslCurrentIndex}, dslWait={data.DslWaitingType}, typedState.count={data.TypedState?.Count ?? 0}, state.count={data.State?.Count ?? 0}");

        OnResetInteractionState?.Invoke();
        Debug.WriteLine($"[SaveDataService] After ResetInteractionState: Dirty={_state.Get<bool>(StateKeys.Scene.Dirty)}, elements.count={_state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements)?.Count ?? 0}");

        // 1. 清除当前所有用户变量（保留 __ 系统变量）
        var allState = _state.GetSnapshot();
        foreach (var (k, _) in allState)
        {
            if (!string.IsNullOrEmpty(k) && !k.StartsWith(StateKeys.SystemPrefix) && !k.StartsWith("_local_"))
                _state.Remove(k);
        }
        Debug.WriteLine($"[SaveDataService] After clear user vars: elements.count={_state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements)?.Count ?? 0}");

        // 1a. 显式清除场景元素（系统变量不会被上面清除，但存档可能不含这些键——
        //     如从 Menu 存档且 __game_scene_elements 为 null 时，null 值会被 FromSaveEntry 跳过，
        //     导致旧 Menu 元素残留。这里先清空，确保存档中的元素能正确覆盖）
        _state.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
        _state.Set(StateKeys.Scene.RuntimeElements, new List<UIElementEntity>());
        Debug.WriteLine($"[SaveDataService] After explicit clear elements: elements.count=0");

        // 2. 恢复存档中的用户状态
        //    优先使用 TypedState（V2 格式，类型安全），回退到 State + ConvertJsonValue（V1 兼容）
        var restoredCount = 0;
        if (data.TypedState != null && data.TypedState.Count > 0)
        {
            // V2 格式：根据类型标识精确还原
            foreach (var (k, entry) in data.TypedState)
            {
                var restored = FromSaveEntry(entry);
                if (restored != null)
                {
                    _state.Set(k, restored);
                    restoredCount++;
                }
            }
        }
        else
        {
            // V1 格式回退：JsonElement 需转换回 .NET 类型
            // data.State 理论上有默认值 new()，但旧存档反序列化可能为 null
            foreach (var (k, v) in data.State ?? new Dictionary<string, object?>())
            {
                // 类型还原：List<UIElementEntity> 在存档中存为 JSON 字符串
                if ((k == StateKeys.Scene.Elements || k == StateKeys.Scene.RuntimeElements) && v is string jsonStr)
                {
                    try
                    {
                        var els = System.Text.Json.JsonSerializer.Deserialize(jsonStr, Abstractions.Serialization.LfJsonContext.Default.ListUIElementEntity);
                        if (els != null) _state.Set(k, els);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SaveDataService] ApplySaveData JSON deserialize failed for key '{k}': {ex.Message}");
                    }
                }
                else
                {
                    _state.Set(k, _jsonConverter.Convert(v));
                }
            }
        }

        Debug.WriteLine($"[SaveDataService] After restore state: restoredCount={restoredCount}, elements.count={_state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements)?.Count ?? 0}");

        // 3. 恢复 SceneStack
        if (_sceneStack != null && data.SceneStackSnapshot != null)
        {
            _sceneStack.Restore(data.SceneStackSnapshot);
        }

        // 3a. 重置场景类型为 Game（存档始终是 Game 场景）
        //     如果从 Menu 场景读档，CurrentType 仍为 Menu，会导致 CreateCheckpoint 跳过
        _state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);

        // 3b. 清除菜单标记和场景渲染状态（读档后不在菜单中，场景由回放重建）
        _state.Set(StateKeys.Scene.MenuReturnTo, (string?)null);
        _state.Set(StateKeys.Scene.GameDslIndex, 0);
        _state.Set(StateKeys.Scene.GameDslWaitingType, "");
        _state.Set(StateKeys.Scene.GameSceneElements, (List<UIElementEntity>?)null);
        _state.Set(StateKeys.Scene.GameRuntimeElements, (List<UIElementEntity>?)null);
        _state.Set(StateKeys.Scene.GameCurrentBackground, (string?)null);
        _state.Set(StateKeys.Scene.CurrentBackground, (string?)null);

        // 3c. 重置 Skip/Auto 模式（读档不应继承读档前的播放状态）
        _state.Set(StateKeys.Playback.SkipActive, false);
        _state.Set(StateKeys.Playback.AutoActive, false);
        _state.Set(StateKeys.Playback.AutoTimer, 0.0);

        // 4. 清除回溯检查点（读档后从当前状态重新开始积累）
        _dslExecutor?.ClearCheckpoints();

        // 4a. 清除对话历史（存档不保存历史，读档后应从空开始）
        _state.Set(StateKeys.History.Entries, new List<DialogHistoryEntry>());

        // 4b. Phase 60-61: 恢复时间事件状态（仅小说世界模式）
        // 回调驱动事件：恢复 firedOneShotIds，场景初始化时重新注册回调
        // 导航驱动事件（兼容旧 API）：直接恢复注册
        if (_options.EnableTimeSystem && _eventScheduler != null)
        {
            _eventScheduler.ApplySaveState(data.TimeEventState);
            if (data.TimeEvents != null)
            {
                _eventScheduler.RegisterEvents(data.TimeEvents);
                Debug.WriteLine($"[SaveDataService] 恢复 {data.TimeEvents.Count} 个导航驱动时间事件");
            }
        }

        // 5. 重新进入场景（恢复场景逻辑执行）
        var sceneName = data.SceneName ?? "";
        _state.Set(StateKeys.Scene.CurrentName, sceneName);
        // 强制 SceneView 重建（即使场景名相同，元素列表已变更）
        _state.Set(StateKeys.Scene.Dirty, true);
        Debug.WriteLine($"[SaveDataService] After set scene+dirty: scene={sceneName}, Dirty=true, elements.count={_state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements)?.Count ?? 0}");

        // 5a. 尝试 C# StoryScript 场景（优先）
        var scriptEntry = TryGetScriptEntry?.Invoke(sceneName);
        if (scriptEntry != null)
        {
            // 合并场景定义（仅补缺，状态已从存档恢复）
            if (scriptEntry.Defines != null)
                MergeIntoState(scriptEntry.Defines, _state);

            // 创建场景级检查点——读档清除了所有检查点，需要为 C# 场景重新创建
            // 这样回溯到此处 = 重新执行整个 StoryScript.Run()
            _dslExecutor?.CreateSceneCheckpoint(sceneName);

            // 使用代次追踪模式启动 Runner——与 NavigateHandler.RunScriptEntryWithGeneration 一致
            // 确保回溯/前进时旧 Runner 能被 CSharpSceneReplayCancelledException 正确终止
            _ = RunScriptEntryWithGeneration(scriptEntry, _state);
            Debug.WriteLine($"[SaveDataService] ApplySaveData: 重新执行 StoryScript [{sceneName}]");
        }
        // 5b. 尝试 DSL 场景（从存档位置恢复执行）
        else if (_storyRegistry != null && _dslExecutor != null)
        {
            // 重新加载 story 文件获取命令列表和标签
            if (_storyRegistry.LoadScene(sceneName))
            {
                var (cmds, lbls) = _storyRegistry.GetCompiledResult(sceneName);
                if (cmds != null && lbls != null)
                {
                    _dslExecutor.LoadCommands(cmds, lbls);
                    // Phase 60: continueGame=false（锚点读取）时从头执行场景
                    var savedIndex = !continueGame ? 0 : (data.DslCurrentIndex >= 0 ? data.DslCurrentIndex : 0);

                    // 回放场景构建命令重建场景状态（场景元素 + 运行时元素 + 背景）
                    // 统一使用 SceneReplayHelper 处理 ShowElementCommand/ShowHideCommand/BgSwitchCommand/SpriteCommand
                    var replayCount = SceneReplayHelper.ReplaySceneState(cmds, savedIndex, _state);
                    Debug.WriteLine($"[SaveDataService] 回放场景: {replayCount} 个场景元素");

                    // Phase 61: continueGame=true 时，重注册 [0, savedIndex) 区间的 SetTimeEventCommand
                    // SceneReplayHelper 只处理视觉命令，不处理事件注册命令。
                    // 如果不重注册，存档点之前的 set_time_event 注册的回调事件将丢失。
                    // continueGame=false 时 savedIndex=0，DSL 从头执行会自然重注册，无需此处处理。
                    // Phase 62: 只重注册存档时实际已注册的事件（检查 TimeEventState.RegisteredIds），
                    //           避免错误注册条件分支中从未执行过的 set_time_event。
                    if (continueGame && savedIndex > 0 && _options.EnableTimeSystem && _eventScheduler != null)
                    {
                        var registeredIds = data.TimeEventState?.RegisteredIds;
                        int reregistered = 0;
                        for (int i = 0; i < savedIndex && i < cmds.Count; i++)
                        {
                            if (cmds[i] is SetTimeEventCommand steCmd)
                            {
                                // 只重注册存档时确实已注册的事件
                                if (registeredIds == null || registeredIds.Contains(steCmd.Id))
                                {
                                    _eventScheduler.RegisterEvent(steCmd.ToRegistration());
                                    reregistered++;
                                }
                            }
                        }
                        if (reregistered > 0)
                            Debug.WriteLine($"[SaveDataService] 重注册 {reregistered} 个回调驱动时间事件");
                    }

                    _state.Set(StateKeys.Dsl.CurrentIndex, savedIndex);
                    _dslExecutor.Start();
                    Debug.WriteLine(
                        $"[SaveDataService] ApplySaveData: 恢复 DSL 场景 [{sceneName}] 从索引 {savedIndex}, commands.count={cmds.Count}");
                    Debug.WriteLine($"[SaveDataService] ===== ApplySaveData END =====");
                }
            }
            else
            {
                Debug.WriteLine(
                    $"[SaveDataService] ApplySaveData: 无法加载 DSL 场景 [{sceneName}]，重定向到标题场景");
                // 容错：场景不存在（如旧存档引用已删除的场景）→ 回到标题
                var titleScene = _options.TitleSceneName;
                _state.Set(StateKeys.Scene.CurrentName, titleScene);
                _state.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
                _state.Set(StateKeys.Scene.Dirty, true);
                _state.Set(StateKeys.Notify.Text, $"存档场景 [{sceneName}] 已不存在，已返回标题");
                _state.Set(StateKeys.Notify.Type, "warning");
                if (_storyRegistry != null && _storyRegistry.LoadScene(titleScene))
                {
                    var (tCmds, tLbls) = _storyRegistry.GetCompiledResult(titleScene);
                    if (tCmds != null && tLbls != null)
                    {
                        _dslExecutor!.LoadCommands(tCmds, tLbls);
                        _dslExecutor.StartFromLabel(titleScene);
                    }
                }
            }
        }
        // 5c. 尝试 SceneRegistry 中的场景实体
        else
        {
            var entity = _sceneRegistry?.FindScene(sceneName);
            if (entity != null)
                _state.Set(StateKeys.Scene.Elements, entity.Elements);
            // C# StoryScript 场景不在 SceneRegistry 中——保留存档恢复的 __scene_elements
        }

        // P2-#15: after_load 钩子（对标 Ren'Py label after_load）
        OnAfterLoad?.Invoke(data);
    }

    /// <inheritdoc/>
    public void SaveSystemState()
    {
        try
        {
            if (_saveService == null) return;
            var typedState = new Dictionary<string, SaveEntry>();
            foreach (var (k, v) in _state.GetSnapshot())
            {
                if (!k.StartsWith(StateKeys.SystemPrefix)) continue;
                if (k.StartsWith(StateKeys.GameTime.Prefix)) continue;
                if (IsTransientSystemKey(k)) continue;
                typedState[k] = ToSaveEntry(v);
            }
            var data = new SaveData
            {
                Name = StateKeys.SystemSaveSlot,
                GameVersion = _options.GameVersion,
                SceneName = "",
                TypedState = typedState
            };
            _saveService.SaveAsync(StateKeys.SystemSaveSlot, data);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveDataService] SaveSystemState failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task LoadSystemStateAsync()
    {
        try
        {
            if (_saveService == null) return;
            var loaded = await _saveService.LoadAsync(StateKeys.SystemSaveSlot);
            if (loaded == null) return;
            if (loaded.TypedState != null && loaded.TypedState.Count > 0)
            {
                foreach (var (k, entry) in loaded.TypedState)
                {
                    if (IsTransientSystemKey(k)) continue;
                    var restored = FromSaveEntry(entry);
                    if (restored != null)
                        _state.Set(k, restored);
                }
            }
            else if (loaded.State != null)
            {
                foreach (var (k, v) in loaded.State)
                {
                    if (IsTransientSystemKey(k)) continue;
                    _state.Set(k, _jsonConverter.Convert(v));
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveDataService] LoadSystemStateAsync failed: {ex.Message}");
        }
    }

    // ========== 私有辅助方法 ==========

    private static bool IsTransientSystemKey(string key) => key switch
    {
        StateKeys.Dialog.Text or StateKeys.Dialog.Speaker
        or StateKeys.Dialog.WaitingSayComplete or StateKeys.Dialog.Complete
        or StateKeys.Menu.Selected or StateKeys.Menu.Options or StateKeys.Menu.Prompt
        or StateKeys.Input.Result or StateKeys.Input.Prompt
        or StateKeys.Transition.Type or StateKeys.Transition.Active
        or StateKeys.Transition.Progress or StateKeys.Transition.OffsetX
        or StateKeys.Transition.OffsetY or StateKeys.Transition.Scale
        or StateKeys.Transition.Elapsed or StateKeys.Transition.Duration
        or StateKeys.Transition.Easing
        or StateKeys.Scene.Dirty or StateKeys.Scene.RuntimeElements
        or StateKeys.Scene.CurrentName or StateKeys.Scene.Elements
        or StateKeys.Scene.GameDslIndex or StateKeys.Scene.GameDslWaitingType
        or StateKeys.Scene.GameSceneElements or StateKeys.Scene.GameRuntimeElements
        or StateKeys.Scene.GameCurrentBackground
        or StateKeys.Audio.CurrentBgmPath
        or StateKeys.Shake.Active or StateKeys.Shake.Intensity
        or StateKeys.Shake.Duration or StateKeys.Shake.Elapsed
        or StateKeys.Shake.OffsetX or StateKeys.Shake.OffsetY
        or StateKeys.Playback.SkipActive or StateKeys.Playback.AutoActive
        or StateKeys.Playback.AutoTimer
        or StateKeys.History.Visible
        or StateKeys.Dialog.TypewriterDone
        or StateKeys.Dialog.Clickable
        or StateKeys.Dialog.Noskip
        or StateKeys.Gallery.Visible
        or StateKeys.Debug.Visible
        or StateKeys.Nvl.Active or StateKeys.Nvl.Text
        or StateKeys.Nvl.Speakers or StateKeys.Nvl.Count
        => true,
        _ => key.Contains(StateKeys.Animation.Prefix),
    };

    /// <summary>
    /// 将运行时值转换为带类型标识的 SaveEntry
    /// </summary>
    private static SaveEntry ToSaveEntry(object? value)
    {
        return value switch
        {
            null => new SaveEntry { Type = SaveEntryTypes.Null, Value = null },
            int i => new SaveEntry { Type = SaveEntryTypes.Int, Value = i },
            long l => new SaveEntry { Type = SaveEntryTypes.Long, Value = l },
            float f => new SaveEntry { Type = SaveEntryTypes.Float, Value = f },
            double d => new SaveEntry { Type = SaveEntryTypes.Double, Value = d },
            bool b => new SaveEntry { Type = SaveEntryTypes.Bool, Value = b },
            string s => new SaveEntry { Type = SaveEntryTypes.String, Value = s },
            decimal dec => new SaveEntry { Type = SaveEntryTypes.Decimal, Value = dec },
            System.DateTime dt => new SaveEntry { Type = SaveEntryTypes.DateTime, Value = dt },
            Guid g => new SaveEntry { Type = SaveEntryTypes.Guid, Value = g },
            List<UIElementEntity> els => new SaveEntry
            {
                Type = SaveEntryTypes.ListUIElement,
                Value = System.Text.Json.JsonSerializer.Serialize(els, Abstractions.Serialization.LfJsonContext.Default.ListUIElementEntity)
            },
            System.Collections.IDictionary dict => new SaveEntry
            {
                Type = SaveEntryTypes.DictStringObject,
                Value = dict.Keys.Cast<object>()
                    .ToDictionary(k => k?.ToString() ?? "", k => dict[k])
            },
            _ => new SaveEntry { Type = SaveEntryTypes.String, Value = value?.ToString() }
        };
    }

    /// <summary>
    /// 从 SaveEntry 还原运行时值（根据类型标识精确还原）
    /// </summary>
    private object? FromSaveEntry(SaveEntry entry)
    {
        if (entry.Value is System.Text.Json.JsonElement je)
        {
            return entry.Type switch
            {
                SaveEntryTypes.Null => null,
                SaveEntryTypes.Int => je.TryGetInt32(out var i) ? i : je.GetInt32(),
                SaveEntryTypes.Long => je.TryGetInt64(out var l) ? l : je.GetInt64(),
                SaveEntryTypes.Float => je.GetSingle(),
                SaveEntryTypes.Double => je.GetDouble(),
                SaveEntryTypes.Decimal => je.GetDecimal(),
                SaveEntryTypes.Bool => je.GetBoolean(),
                SaveEntryTypes.String => je.GetString(),
                SaveEntryTypes.DateTime => je.TryGetDateTime(out var dt) ? dt : je.GetDateTime(),
                SaveEntryTypes.Guid => je.TryGetGuid(out var g) ? g : je.GetGuid(),
                SaveEntryTypes.ListUIElement => TryDeserializeUIElements(je),
                SaveEntryTypes.DictStringObject => _jsonConverter.Convert(je),
                _ => _jsonConverter.Convert(je)
            };
        }
        return entry.Value;
    }

    /// <summary>
    /// 尝试反序列化 UIElementEntity 列表
    /// </summary>
    private static List<UIElementEntity>? TryDeserializeUIElements(System.Text.Json.JsonElement je)
    {
        try
        {
            var jsonStr = je.ValueKind == System.Text.Json.JsonValueKind.String
                ? je.GetString()
                : je.GetRawText();
            if (string.IsNullOrEmpty(jsonStr)) return null;
            return System.Text.Json.JsonSerializer.Deserialize(jsonStr, Abstractions.Serialization.LfJsonContext.Default.ListUIElementEntity);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SaveDataService] TryDeserializeUIElements failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 深合并场景变量定义到状态容器（仅补缺不覆盖，标量类型不匹配时修复为定义默认值）
    /// </summary>
    internal static void MergeIntoState(Dictionary<string, object?> dict, IStateContainer state, string prefix = "")
    {
        foreach (var (k, v) in dict)
        {
            var key = string.IsNullOrEmpty(prefix) ? k : $"{prefix}.{k}";

            Dictionary<string, object?>? subDict = null;
            if (v is Dictionary<string, object?> dso)
                subDict = dso;
            else if (v is System.Collections.IDictionary rawDict)
            {
                subDict = new Dictionary<string, object?>();
                foreach (System.Collections.DictionaryEntry entry in rawDict)
                    subDict[entry.Key?.ToString() ?? ""] = entry.Value;
            }

            if (subDict != null)
            {
                var existing = state.Get<object>(key);
                if (existing is System.Collections.IDictionary)
                    MergeIntoState(subDict, state, key);
                else
                    state.Set(key, subDict);
            }
            else
            {
                var existing = state.Get<object>(key);
                if (existing == null || existing.GetType() != v?.GetType())
                    state.Set(key, v);
            }
        }
    }

    /// <summary>
    /// 启动 C# StoryScript Runner，设置 AsyncLocal 回放代次以支持回溯取消
    /// <para>与 NavigateHandler.RunScriptEntryWithGeneration 逻辑一致——确保读档恢复的
    /// C# 场景在回溯/前进时能通过 CSharpSceneReplayCancelledException 正确终止旧 Runner。</para>
    /// </summary>
    private static async Task RunScriptEntryWithGeneration(SceneScriptEntry scriptEntry, IStateContainer state)
    {
        var gen = state.Get<int>(StateKeys.Dsl.CSharpReplayGeneration);
        GameController.CSharpReplayGen.Value = gen;
        try
        {
            await scriptEntry.Runner();
        }
        catch (CSharpSceneReplayCancelledException)
        {
            // 回溯/前进取消了此场景——Runner 已被异常终止
            Debug.WriteLine($"[SaveDataService] C# 场景 [{scriptEntry.SceneName}] 被回溯/前进取消");
        }
        finally
        {
            GameController.CSharpReplayGen.Value = 0;
        }
    }
}
