using System.Diagnostics;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Abstractions.Scripting;

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

    /// <summary>截图回调（由 GameLoop 在 SetSceneView 时设置）</summary>
    public Func<byte[]?>? CaptureThumbnail { get; set; }

    /// <summary>脚本入口查询回调（由 GameLoop 设置，用于 C# StoryScript 场景恢复）</summary>
    public Func<string, SceneScriptEntry?>? TryGetScriptEntry { get; set; }

    /// <summary>重置交互状态回调（由 GameLoop 设置，ApplySaveData 开头调用）</summary>
    public Action? OnResetInteractionState { get; set; }

    public SaveDataService(
        IStateContainer state,
        IJsonValueConverter jsonConverter,
        LingFanEngineOptions options,
        ISaveService? saveService = null,
        ISceneStack? sceneStack = null,
        ISceneRegistry? sceneRegistry = null,
        IStoryRegistry? storyRegistry = null,
        IDslExecutor? dslExecutor = null)
    {
        _state = state;
        _jsonConverter = jsonConverter;
        _options = options;
        _saveService = saveService;
        _sceneStack = sceneStack;
        _sceneRegistry = sceneRegistry;
        _storyRegistry = storyRegistry;
        _dslExecutor = dslExecutor;
    }

    /// <inheritdoc/>
    public SaveData? BuildSaveData()
    {
        var currentSceneName = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";

        // 拒绝在 Menu/UI 场景存档
        var currentType = (SceneType)_state.Get<int>(StateKeys.Scene.CurrentType);
        if (currentType != SceneType.Game)
        {
            Debug.WriteLine($"[SaveDataService] 拒绝在 {currentType} 场景存档: {currentSceneName}");
            return null;
        }

        // 1. 收集全量用户状态（排除 __ 系统变量）
        var stateDict = new Dictionary<string, object?>();
        var typedState = new Dictionary<string, SaveEntry>();
        var allState = _state.GetSnapshot();
        foreach (var (k, v) in allState)
        {
            // 排除 __ 系统变量和 _local_ 局部变量（保留：__game_time_*、__runtime_elements、__current_background）
            if (!string.IsNullOrEmpty(k) && !k.StartsWith("_local_")
                && (!k.StartsWith(StateKeys.SystemPrefix) || (_options.EnableTimeSystem && k.StartsWith(StateKeys.GameTime.Prefix))
                     || k == StateKeys.Scene.RuntimeElements || k == StateKeys.Scene.CurrentBackground
                     || k == StateKeys.Scene.Elements))
            {
                stateDict[k] = v;
                // 同时构建 TypedState（类型安全的 V2 格式）
                typedState[k] = ToSaveEntry(v);
            }
        }

        // 类型保留：List<UIElementEntity> → 源生成器序列化为 JSON 字符串（State 旧格式兼容）
        foreach (var key in new[] { StateKeys.Scene.Elements, StateKeys.Scene.RuntimeElements })
        {
            if (stateDict.TryGetValue(key, out var v) && v is List<UIElementEntity> els)
                stateDict[key] = System.Text.Json.JsonSerializer.Serialize(els, Abstractions.Serialization.LfJsonContext.Default.ListUIElementEntity);
        }

        // 2. 收集 SceneStack 快照
        var stackSnapshot = _sceneStack?.Snapshot?.ToList() ?? new List<SceneSnapshot>();

        byte[]? thumb = null;
        try { thumb = CaptureThumbnail?.Invoke(); }
        catch (Exception ex) { Debug.WriteLine($"[SaveDataService] CaptureThumbnail failed: {ex.Message}"); }

        var data = new SaveData
        {
            GameVersion = _options.GameVersion,
            Name = _options.SaveNameFormatter?.Invoke(currentSceneName) ?? $"存档 - {currentSceneName}",
            SceneName = currentSceneName,
            State = stateDict,
            TypedState = typedState,
            SceneStackSnapshot = stackSnapshot,
            Thumbnail = thumb,
            DslCurrentIndex = _state.Get<int>(StateKeys.Dsl.CurrentIndex),
            DslWaitingType = _state.Get<string>(StateKeys.Dsl.WaitingType) ?? "",
            SceneType = SceneType.Game,
        };

        return data;
    }

    /// <inheritdoc/>
    public void ApplySaveData(SaveData data)
    {
        OnResetInteractionState?.Invoke();

        // 1. 清除当前所有用户变量（保留 __ 系统变量）
        var allState = _state.GetSnapshot();
        foreach (var (k, _) in allState)
        {
            if (!string.IsNullOrEmpty(k) && !k.StartsWith(StateKeys.SystemPrefix) && !k.StartsWith("_local_"))
                _state.Remove(k);
        }

        // 2. 恢复存档中的用户状态
        //    优先使用 TypedState（V2 格式，类型安全），回退到 State + ConvertJsonValue（V1 兼容）
        if (data.TypedState != null && data.TypedState.Count > 0)
        {
            // V2 格式：根据类型标识精确还原
            foreach (var (k, entry) in data.TypedState)
            {
                var restored = FromSaveEntry(entry);
                if (restored != null)
                    _state.Set(k, restored);
            }
        }
        else
        {
            // V1 格式回退：JsonElement 需转换回 .NET 类型
            foreach (var (k, v) in data.State)
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

        // 3. 恢复 SceneStack
        if (_sceneStack != null && data.SceneStackSnapshot != null)
        {
            _sceneStack.Restore(data.SceneStackSnapshot);
        }

        // 4. 清除回溯检查点（读档后从当前状态重新开始积累）
        _dslExecutor?.ClearCheckpoints();

        // 5. 重新进入场景（恢复场景逻辑执行）
        var sceneName = data.SceneName ?? "";
        _state.Set(StateKeys.Scene.CurrentName, sceneName);

        // 5a. 尝试 C# StoryScript 场景（优先）
        var scriptEntry = TryGetScriptEntry?.Invoke(sceneName);
        if (scriptEntry != null)
        {
            // 合并场景定义（仅补缺，状态已从存档恢复）
            if (scriptEntry.Defines != null)
                MergeIntoState(scriptEntry.Defines, _state);
            // 重新执行场景脚本（状态已恢复，脚本会根据当前状态走正确分支）
            _ = scriptEntry.Runner();
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
                    // 恢复执行位置
                    var savedIndex = data.DslCurrentIndex >= 0 ? data.DslCurrentIndex : 0;
                    // 如果存档时正在等待 dialog/menu/input，回退一步重新展示
                    var waitingType = data.DslWaitingType ?? "";
                    if ((waitingType == StateKeys.Dsl.WaitingTypes.Dialog || waitingType == StateKeys.Dsl.WaitingTypes.Menu || waitingType == StateKeys.Dsl.WaitingTypes.Input)
                        && savedIndex > 0)
                        savedIndex--;
                    _state.Set(StateKeys.Dsl.CurrentIndex, savedIndex);
                    // 启动异步执行（从恢复的索引开始）
                    _dslExecutor.Start();
                    Debug.WriteLine(
                        $"[SaveDataService] ApplySaveData: 恢复 DSL 场景 [{sceneName}] 从索引 {savedIndex}");
                }
            }
            else
            {
                Debug.WriteLine(
                    $"[SaveDataService] ApplySaveData: 无法加载 DSL 场景 [{sceneName}]");
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
    }

    /// <inheritdoc/>
    public void SaveSystemState()
    {
        try
        {
            if (_saveService == null) return;
            var state = new Dictionary<string, object?>();
            var typedState = new Dictionary<string, SaveEntry>();
            foreach (var (k, v) in _state.GetSnapshot())
            {
                if (!k.StartsWith(StateKeys.SystemPrefix)) continue;
                if (k.StartsWith(StateKeys.GameTime.Prefix)) continue;
                if (IsTransientSystemKey(k)) continue;
                state[k] = v;
                typedState[k] = ToSaveEntry(v);
            }
            var data = new SaveData
            {
                Name = StateKeys.SystemSaveSlot,
                GameVersion = _options.GameVersion,
                SceneName = "",
                State = state,
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
        or StateKeys.Audio.CurrentBgmPath
        or StateKeys.Shake.Active or StateKeys.Shake.Intensity
        or StateKeys.Shake.Duration or StateKeys.Shake.Elapsed
        or StateKeys.Shake.OffsetX or StateKeys.Shake.OffsetY
        or StateKeys.Playback.SkipActive or StateKeys.Playback.AutoActive
        or StateKeys.Playback.AutoTimer
        or StateKeys.History.Visible
        or StateKeys.Dialog.TypewriterDone
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
}
