using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// DSL 执行器——异步优先的命令执行器
/// <para>RunAsync 为主执行循环，遇到交互命令（say/menu/input/wait）时
/// 使用 async/await 天然等待，无需帧轮询状态标记。</para>
/// <para>所有状态保存在状态容器中，不维护私有内部状态（除 CancellationTokenSource）。</para>
/// <para>支持统一线性回溯时间线（Phase 16/16.1）：检查点列表 + CurrentIndex 前沿模型，
/// say/menu/input/wait/scene_idle/navigate 创建检查点（全量状态快照），
/// 回溯时取消当前 RunAsync、恢复检查点状态、重启 RunAsync。</para>
/// </summary>
public class DslExecutor : IDslExecutor
{
    private readonly IStateContainer _state;
    private readonly ICommandPipeline _pipeline;
    private readonly LingFanEngineOptions _options;
    private readonly IAsyncWaitService _waitService;
    private readonly IEventScheduler? _eventScheduler;
    private IStoryRegistry? _storyRegistry;

    /// <summary>异步执行取消令牌（线程安全——使用 Interlocked.Exchange 原子替换）</summary>
    private CancellationTokenSource? _cts;
    /// <summary>当前运行中的执行任务（线程安全——使用 Interlocked.Exchange 原子替换）</summary>
    private Task? _runTask;

    /// <summary>回溯自身相关的键，快照时排除</summary>
    private static readonly HashSet<string> s_rollbackKeys = new()
    {
        StateKeys.Rollback.Checkpoints,
        StateKeys.Rollback.CurrentIndex,
        StateKeys.Rollback.IsActive,
        StateKeys.Rollback.IsReplay,
        StateKeys.Rollback.BlockedUntil,
        StateKeys.Playback.SeenSayIndices,
        StateKeys.Dsl.CSharpReplayGeneration,
        // Phase 41: Skip/Auto 是播放模式状态，不是游戏内容——回溯不应恢复它们
        // 回溯 = 浏览历史，Skip/Auto 应保持回溯前的值（通常已关闭）
        StateKeys.Playback.SkipActive,
        StateKeys.Playback.AutoActive,
        StateKeys.Playback.AutoTimer,
    };

    /// <summary>C# 场景回溯回调（由 GameLoop 设置，回溯到 C# 场景时调用）</summary>
    public Func<string, Task>? OnCSharpSceneReplay { get; set; }

    public DslExecutor(IStateContainer state, ICommandPipeline pipeline, LingFanEngineOptions? options = null,
        IAsyncWaitService? waitService = null,
        IEventScheduler? eventScheduler = null)
    {
        _state = state;
        _pipeline = pipeline;
        _options = options ?? new LingFanEngineOptions();
        // waitService 可为 null（仅测试场景——测试不执行 RunAsync 中的交互等待方法）
        _waitService = waitService!;
        _eventScheduler = eventScheduler;
    }

    /// <inheritdoc/>
    public bool IsRunning => _runTask is { IsCompleted: false };

    /// <inheritdoc/>
    public void SetStoryRegistry(IStoryRegistry registry)
    {
        _storyRegistry = registry;
    }

    /// <inheritdoc/>
    public void LoadCommands(IReadOnlyList<ICommand> commands, IReadOnlyDictionary<string, int>? labels = null, bool preserveCheckpoints = false)
    {
        Stop();
        _state.Set(StateKeys.Dsl.Commands, commands.ToList());
        _state.Set(StateKeys.Dsl.Labels, labels ?? new Dictionary<string, int>());
        _state.Set(StateKeys.Dsl.CurrentIndex, 0);
        _state.Set(StateKeys.Dsl.Executing, false);
        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Dsl.TotalCommands, commands.Count);

        if (!preserveCheckpoints)
            ClearCheckpoints();
    }

    /// <inheritdoc/>
    public void Start()
    {
        Stop();
        _state.Set(StateKeys.Dsl.Executing, true);
        _state.Set(StateKeys.Dsl.WaitingType, "");
        BeginRunAsync();
    }

    /// <inheritdoc/>
    public void StartFromLabel(string label)
    {
        Stop();

        var labels = _state.Get<IReadOnlyDictionary<string, int>>(StateKeys.Dsl.Labels) ??
                     _state.Get<Dictionary<string, int>>(StateKeys.Dsl.Labels);
        var commands = _state.Get<List<ICommand>>(StateKeys.Dsl.Commands);

        // 当前已加载的 labels 中有该 label
        if (labels != null && commands != null && labels.TryGetValue(label, out var idx))
        {
            _state.Set(StateKeys.Dsl.CurrentIndex, idx);
            _state.Set(StateKeys.Dsl.Executing, true);
            _state.Set(StateKeys.Dsl.WaitingType, "");
            BeginRunAsync();
            return;
        }

        // 当前 labels 中没有——通过 StoryRegistry 自动查找并加载所属文件
        if (_storyRegistry != null)
        {
            var filePath = _storyRegistry.FindFileByLabel(label);
            if (filePath != null && _storyRegistry.EnsureLabelLoaded(label))
            {
                var (cmds, lbls) = _storyRegistry.GetCompiledResultByFile(filePath);
                if (cmds != null && lbls != null && lbls.TryGetValue(label, out var idx2))
                {
                    LoadCommands(cmds, lbls, preserveCheckpoints: true);
                    _state.Set(StateKeys.Dsl.CurrentIndex, idx2);
                    _state.Set(StateKeys.Dsl.Executing, true);
                    _state.Set(StateKeys.Dsl.WaitingType, "");
                    BeginRunAsync();
                    System.Diagnostics.Debug.WriteLine($"[DslExecutor] 自动加载 label [{label}] 来自 {filePath}");
                    return;
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[DslExecutor] Label [{label}] 未找到");
    }

    /// <inheritdoc/>
    public void Stop()
    {
        // 线程安全：原子取消并清除引用
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
        cts?.Dispose();
        Interlocked.Exchange(ref _runTask, null);
        _state.Set(StateKeys.Dsl.Executing, false);
        _state.Set(StateKeys.Dsl.WaitingType, "");
    }

    /// <summary>
    /// 启动 RunAsync 任务（fire-and-forget）
    /// </summary>
    private void BeginRunAsync()
    {
        // 线程安全：先取消并清理旧 CTS/Task，再创建新的
        var oldCts = Interlocked.Exchange(ref _cts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();
        Interlocked.Exchange(ref _runTask, null);

        var newCts = new CancellationTokenSource();
        Interlocked.Exchange(ref _cts, newCts);
        var ct = newCts.Token;
        _runTask = Task.Run(() => RunAsync(ct), ct);
    }

    // ========== 主执行循环（异步优先） ==========

    /// <summary>
    /// 异步执行循环——遇到交互命令时用 async/await 天然等待
    /// </summary>
    private async Task RunAsync(CancellationToken ct)
    {
        var commands = _state.Get<List<ICommand>>(StateKeys.Dsl.Commands);
        if (commands == null) return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 处理待处理的时间事件回调（在主脚本命令之间执行）
                await ProcessPendingTimeEvents(ct);
                if (ct.IsCancellationRequested) return;

                var currentIndex = _state.Get<int>(StateKeys.Dsl.CurrentIndex);

                if (currentIndex >= commands.Count)
                {
                    // 命令列表耗尽——场景元素已全部添加（按钮可见），用户将与此场景交互
                    // 创建检查点：回溯到此处 = 直接看到完整场景（含按钮），无需重新点击 say
                    if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                    {
                        var cps = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
                        if (cps == null || cps.Count == 0 || cps[^1].CommandIndex != currentIndex)
                        {
                            _state.Set(StateKeys.Dialog.Text, "");
                            _state.Set(StateKeys.Dialog.Speaker, "");
                            _state.Set(StateKeys.Dialog.Complete, false);
                            CreateCheckpoint(currentIndex, "scene_idle");
                            AdvanceRollbackFrontier();
                        }
                    }
                    _state.Set(StateKeys.Rollback.IsActive, false);
                    _state.Set(StateKeys.Rollback.IsReplay, false);
                    _state.Set(StateKeys.Dsl.Executing, false);
                    break;
                }

                var cmd = commands[currentIndex];

                switch (cmd)
                {
                    // ========== 交互命令（async/await 等待）==========

                    case ShowDialogCommand dialog:
                    {
                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Dialog);

                        _ = _pipeline.SendAsync(cmd);
                        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Dialog);

                        await WaitForDialogComplete(ct);
                        if (ct.IsCancellationRequested) return;

                        _state.Set(StateKeys.Dsl.WaitingType, "");
                        // 重置 Clickable——防止 say clickable=true 的状态泄漏到后续非 say 命令
                        _state.Set(StateKeys.Dialog.Clickable, false);
                        // Phase 37: 重置 Noskip——防止 say noskip=true 的状态泄漏
                        _state.Set(StateKeys.Dialog.Noskip, false);

                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                        }

                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        AdvanceRollbackFrontier();
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case WaitCommand wait:
                    {
                        var waitType = wait.IsSkipable
                            ? StateKeys.Dsl.WaitingTypes.WaitSkipable
                            : StateKeys.Dsl.WaitingTypes.Wait;

                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, waitType);

                        _state.Set(StateKeys.Dsl.WaitingType, waitType);
                        _state.Set(StateKeys.Dsl.WaitUntil, Environment.TickCount64 / 1000.0 + wait.Seconds);
                        _state.Set(StateKeys.Dsl.WaitDuration, wait.Seconds);

                        if (wait.IsSkipable)
                        {
                            _state.Set(StateKeys.Dialog.Text, "");
                            _state.Set(StateKeys.Dialog.Speaker, "");
                            _state.Set(StateKeys.Dialog.Clickable, false);
                            _state.Set(StateKeys.Dialog.Complete, false);

                            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            var delayTask = Task.Delay(TimeSpan.FromSeconds(wait.Seconds), waitCts.Token);
                            var clickTask = WaitForDialogComplete(waitCts.Token);
                            var winner = await Task.WhenAny(delayTask, clickTask);
                            waitCts.Cancel();

                            _state.Set(StateKeys.Dialog.Complete, false);

                            if (ct.IsCancellationRequested) return;
                        }
                        else
                        {
                            try { await Task.Delay(TimeSpan.FromSeconds(wait.Seconds), ct); }
                            catch (OperationCanceledException) { return; }
                        }

                        if (ct.IsCancellationRequested) return;
                        _state.Set(StateKeys.Dsl.WaitingType, "");

                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        AdvanceRollbackFrontier();
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case HardPauseCommand:
                    {
                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Pause);

                        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Pause);
                        _state.Set(StateKeys.Dialog.Text, "");
                        _state.Set(StateKeys.Dialog.Speaker, "");
                        _state.Set(StateKeys.Dialog.Clickable, false);
                        _state.Set(StateKeys.Dialog.Complete, false);

                        await WaitForDialogComplete(ct);
                        if (ct.IsCancellationRequested) return;

                        _state.Set(StateKeys.Dsl.WaitingType, "");

                        var hpRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (hpRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        AdvanceRollbackFrontier();
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case MenuCommand menu:
                    {
                        _state.Set(StateKeys.Menu.Prompt, menu.Prompt);
                        _state.Set<object>(StateKeys.Menu.Options, menu.Options.Select(o => o.Text).ToArray());
                        _state.Set(StateKeys.Menu.Selected, -1);
                        _state.Set(StateKeys.Menu.DslTargets, string.Join(",", menu.Options.Select(o => o.TargetLabel)));
                        _state.Set(StateKeys.Menu.DslTexts, string.Join(",", menu.Options.Select(o => o.Text)));

                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Menu);

                        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Menu);

                        var selectedIdx = await WaitForMenuSelection(ct);
                        if (ct.IsCancellationRequested) return;

                        _state.Set(StateKeys.Dsl.WaitingType, "");
                        _state.Set(StateKeys.Menu.Prompt, "");
                        _state.Set<object>(StateKeys.Menu.Options, Array.Empty<string>());
                        _state.Set(StateKeys.Menu.Selected, -1);
                        _state.Set(StateKeys.Menu.DslTargets, "");
                        _state.Set(StateKeys.Menu.DslTexts, "");

                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        AdvanceRollbackFrontier();

                        if (selectedIdx >= 0 && selectedIdx < menu.Options.Count)
                        {
                            var targetLabel = menu.Options[selectedIdx].TargetLabel;
                            var labels = _state.Get<Dictionary<string, int>>(StateKeys.Dsl.Labels);
                            if (labels != null && labels.TryGetValue(targetLabel, out var idx))
                            {
                                _state.Set(StateKeys.Dsl.CurrentIndex, idx);
                                continue;
                            }
                        }
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case InputCommand input:
                    {
                        _state.Set(StateKeys.Input.Prompt, input.Prompt);
                        _state.Set(StateKeys.Input.DslStore, input.StoreKey);
                        _state.Set<object>(StateKeys.Input.Options, input.Options ?? Array.Empty<string>());
                        _state.Set<object?>(StateKeys.Input.Result, null);

                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Input);

                        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Input);

                        var inputValue = await WaitForInput(ct);
                        if (ct.IsCancellationRequested) return;

                        _state.Set(StateKeys.Dsl.WaitingType, "");
                        _state.Set(StateKeys.Input.Prompt, "");
                        _state.Set(StateKeys.Input.DslStore, "");
                        _state.Set<object>(StateKeys.Input.Options, Array.Empty<string>());

                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        AdvanceRollbackFrontier();

                        if (!string.IsNullOrEmpty(input.StoreKey))
                            _state.Set(input.StoreKey, inputValue);

                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case EndCommand:
                    {
                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                        {
                            var endCps = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
                            if (endCps == null || endCps.Count == 0 || endCps[^1].CommandIndex != currentIndex)
                            {
                                _state.Set(StateKeys.Dialog.Text, "");
                                _state.Set(StateKeys.Dialog.Speaker, "");
                                _state.Set(StateKeys.Dialog.Complete, false);
                                CreateCheckpoint(currentIndex, "scene_idle");
                                AdvanceRollbackFrontier();
                            }
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        _state.Set(StateKeys.Dsl.Executing, false);
                        _state.Set(StateKeys.Dialog.Text, "");
                        return;
                    }

                    // ========== 控制流命令（同步处理）==========

                    case JumpCommand jmp:
                        if (jmp.TargetIndex >= 0 && jmp.TargetIndex < commands.Count)
                            _state.Set(StateKeys.Dsl.CurrentIndex, jmp.TargetIndex);
                        else
                            _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        continue;

                    case BranchCommand br:
                        if (br.Condition != null)
                        {
                            var conditionMet = DslExpressionEvaluator.EvaluateBool(br.Condition, _state);
                            _state.Set(StateKeys.Dsl.CurrentIndex,
                                currentIndex + (conditionMet ? 1 : br.SkipCount + 1));
                        }
                        else
                            _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + br.SkipCount + 1);
                        continue;

                    case CallCommand call:
                    {
                        var callStack = _state.Get<List<int>>(StateKeys.CallStack.Stack) ?? new List<int>();
                        callStack.Add(currentIndex + 1);
                        _state.Set(StateKeys.CallStack.Stack, callStack);
                        var labels = _state.Get<Dictionary<string, int>>(StateKeys.Dsl.Labels);
                        if (labels != null && labels.TryGetValue(call.TargetLabel, out var callIdx))
                            _state.Set(StateKeys.Dsl.CurrentIndex, callIdx);
                        else
                            _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        continue;
                    }

                    case ReturnCommand:
                    {
                        var retStack = _state.Get<List<int>>(StateKeys.CallStack.Stack);
                        if (retStack != null && retStack.Count > 0)
                        {
                            var retIdx = retStack[^1];
                            retStack.RemoveAt(retStack.Count - 1);
                            _state.Set(StateKeys.Dsl.CurrentIndex, retIdx);
                        }
                        else
                        {
                            _state.Set(StateKeys.Dsl.Executing, false);
                            return;
                        }
                        continue;
                    }

                    // ========== 同步命令 ==========

                    case SetVariableCommand sv:
                        if (sv.IsDefine && _state.ContainsKey(sv.Key))
                        {
                            // define ... once：跳过
                        }
                        else if (sv.Value is DslForLengthPlaceholder forLen)
                        {
                            var source = DslExpressionEvaluator.Evaluate(forLen.SourceExpr, _state);
                            var len = source switch
                            {
                                string s => s.Length,
                                System.Collections.IList list => list.Count,
                                System.Collections.IEnumerable en => en.Cast<object?>().Count(),
                                _ => 0
                            };
                            _state.Set(sv.Key, len);
                        }
                        else if (sv.Value is DslForIndexPlaceholder forIdx)
                        {
                            var source = DslExpressionEvaluator.Evaluate(forIdx.SourceExpr, _state);
                            var index = _state.Get<int>(forIdx.IndexVar);
                            object? element = null;
                            if (source is System.Collections.IList list && index >= 0 && index < list.Count)
                                element = list[index]!;
                            _state.Set(sv.Key, element);
                        }
                        else if (sv.Value is DslExpressionPlaceholder placeholder)
                        {
                            var result = DslExpressionEvaluator.Evaluate(placeholder.Expression, _state);
                            _state.Set(sv.Key, result);
                        }
                        else
                        {
                            _state.Set(sv.Key, sv.Value);
                        }
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;

                    case TransitionCommand:
                        _ = _pipeline.SendAsync(cmd);
                        await WaitForTransitionComplete(ct);
                        if (ct.IsCancellationRequested) return;
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;

                    case ShowElementCommand se:
                        {
                            ApplyStyleIfExists(se.Element);
                            var elements = _state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements) ?? new List<UIElementEntity>();
                            elements.Add(se.Element);
                            _state.Set(StateKeys.Scene.Elements, elements);
                            _state.Set(StateKeys.Scene.Dirty, true);
                            _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                            break;
                        }

                    case CallScreenCommand cs:
                        {
                            // Phase 24: 设置传入参数
                            if (cs.Params != null)
                                _state.Set(StateKeys.Screen.Params, cs.Params);
                            else
                                _state.Set<object?>(StateKeys.Screen.Params, null);

                            if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                                CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.CallScreen);

                            _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.CallScreen);
                            _state.Set<object?>(StateKeys.Screen.Result, null);
                            _ = _pipeline.SendAsync(new NavigateCommand { Path = cs.SceneName });
                            await WaitForScreenResult(ct);
                            if (ct.IsCancellationRequested) return;

                            _state.Set(StateKeys.Dsl.WaitingType, "");

                            var csRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                            if (csRollback && CanRollforward())
                            {
                                if (Rollforward())
                                    return;
                            }
                            _state.Set(StateKeys.Rollback.IsActive, false);
                            _state.Set(StateKeys.Rollback.IsReplay, false);
                            AdvanceRollbackFrontier();

                            if (!string.IsNullOrEmpty(cs.StoreKey))
                            {
                                var screenResult = _state.Get<string?>(StateKeys.Screen.Result);
                                _state.Set(cs.StoreKey, screenResult);
                            }
                            _state.Set<object?>(StateKeys.Screen.Result, null);
                            _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                            break;
                        }

                    case SaveLoadCommand slCmd when !slCmd.IsSave:
                        _ = _pipeline.SendAsync(slCmd);
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        // 标记执行结束——ApplySaveData 异步执行，期间 DslExecutor 不应处于 Executing 状态
                        _state.Set(StateKeys.Dsl.Executing, false);
                        return;

                    case NavigateCommand:
                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                        {
                            var navCps = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
                            if (navCps == null || navCps.Count == 0 || navCps[^1].CommandIndex != currentIndex + 1)
                            {
                                _state.Set(StateKeys.Dialog.Text, "");
                                _state.Set(StateKeys.Dialog.Speaker, "");
                                _state.Set(StateKeys.Dialog.Complete, false);
                                CreateCheckpoint(currentIndex + 1, "navigate");
                                AdvanceRollbackFrontier();
                            }
                        }
                        _ = _pipeline.SendAsync(cmd);
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;

                    default:
                        _ = _pipeline.SendAsync(cmd);
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消（回溯/停止/新加载）
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DslExecutor] RunAsync error: {ex}");
            _state.Set(StateKeys.Dsl.Executing, false);
        }
    }

    // ========== 时间事件回调执行 ==========

    /// <summary>
    /// 处理待处理的时间事件（在主脚本命令之间执行）
    /// <para>DslExecutor 在 RunAsync 循环顶部调用此方法。</para>
    /// <para>对每个事件：C# 回调直接 await，DSL 命令逐条执行（含交互等待）。</para>
    /// </summary>
    private async Task ProcessPendingTimeEvents(CancellationToken ct)
    {
        if (_eventScheduler == null) return;

        while (_eventScheduler.TryDequeuePendingEvent(out var evt) && evt != null)
        {
            if (ct.IsCancellationRequested) return;

            // 检查条件表达式
            if (!string.IsNullOrWhiteSpace(evt.Condition))
            {
                try
                {
                    if (!DslExpressionEvaluator.EvaluateBool(evt.Condition, _state))
                        continue;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[DslExecutor] 时间事件条件求值失败 [{evt.Id}]: {ex.Message}");
                    continue;
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"[DslExecutor] 执行时间事件 [{evt.Id}] - {evt.Description ?? "(无描述)"}");

            try
            {
                if (evt.Callback != null)
                {
                    // C# 回调
                    await evt.Callback();
                }
                else if (evt.Commands != null && evt.Commands.Count > 0)
                {
                    // DSL 命令——逐条执行（含交互等待）
                    foreach (var cmd in evt.Commands)
                    {
                        if (ct.IsCancellationRequested) return;
                        await ExecuteTimeEventCommandAsync(cmd, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DslExecutor] 时间事件执行异常 [{evt.Id}]: {ex.Message}");
            }

            // 标记单次事件已触发
            if (evt.IsOneShot)
            {
                _eventScheduler.MarkFired(evt.Id);
            }
        }
    }

    /// <summary>
    /// 执行时间事件中的单条命令（复用 DslExecutor 的交互等待逻辑）
    /// </summary>
    private async Task ExecuteTimeEventCommandAsync(ICommand cmd, CancellationToken ct)
    {
        switch (cmd)
        {
            case ShowDialogCommand:
                _ = _pipeline.SendAsync(cmd);
                _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Dialog);
                await WaitForDialogComplete(ct);
                _state.Set(StateKeys.Dsl.WaitingType, "");
                _state.Set(StateKeys.Dialog.Clickable, false);
                _state.Set(StateKeys.Dialog.Noskip, false);
                break;

            case WaitCommand wait:
                if (wait.IsSkipable)
                {
                    _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.WaitSkipable);
                    _state.Set(StateKeys.Dialog.Text, "");
                    _state.Set(StateKeys.Dialog.Speaker, "");
                    _state.Set(StateKeys.Dialog.Clickable, false);
                    _state.Set(StateKeys.Dialog.Complete, false);

                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var delayTask = Task.Delay(TimeSpan.FromSeconds(wait.Seconds), waitCts.Token);
                    var clickTask = WaitForDialogComplete(waitCts.Token);
                    await Task.WhenAny(delayTask, clickTask);
                    waitCts.Cancel();

                    _state.Set(StateKeys.Dialog.Complete, false);
                    _state.Set(StateKeys.Dsl.WaitingType, "");
                }
                else
                {
                    try { await Task.Delay(TimeSpan.FromSeconds(wait.Seconds), ct); }
                    catch (OperationCanceledException) { return; }
                }
                break;

            case HardPauseCommand:
                _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Pause);
                _state.Set(StateKeys.Dialog.Text, "");
                _state.Set(StateKeys.Dialog.Speaker, "");
                _state.Set(StateKeys.Dialog.Clickable, false);
                _state.Set(StateKeys.Dialog.Complete, false);
                await WaitForDialogComplete(ct);
                _state.Set(StateKeys.Dsl.WaitingType, "");
                break;

            case TransitionCommand:
                _ = _pipeline.SendAsync(cmd);
                await WaitForTransitionComplete(ct);
                break;

            case MenuCommand menu:
                _state.Set(StateKeys.Menu.Prompt, menu.Prompt);
                _state.Set<object>(StateKeys.Menu.Options, menu.Options.Select(o => o.Text).ToArray());
                _state.Set(StateKeys.Menu.Selected, -1);
                _state.Set(StateKeys.Menu.DslTargets, string.Join(",", menu.Options.Select(o => o.TargetLabel)));
                _state.Set(StateKeys.Menu.DslTexts, string.Join(",", menu.Options.Select(o => o.Text)));
                _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Menu);
                _ = await WaitForMenuSelection(ct);
                _state.Set(StateKeys.Dsl.WaitingType, "");
                _state.Set(StateKeys.Menu.Prompt, "");
                _state.Set<object>(StateKeys.Menu.Options, Array.Empty<string>());
                _state.Set(StateKeys.Menu.Selected, -1);
                _state.Set(StateKeys.Menu.DslTargets, "");
                _state.Set(StateKeys.Menu.DslTexts, "");
                break;

            case InputCommand input:
                _state.Set(StateKeys.Input.Prompt, input.Prompt);
                _state.Set(StateKeys.Input.DslStore, input.StoreKey);
                _state.Set<object>(StateKeys.Input.Options, input.Options ?? Array.Empty<string>());
                _state.Set<object?>(StateKeys.Input.Result, null);
                _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Input);
                _ = await WaitForInput(ct);
                _state.Set(StateKeys.Dsl.WaitingType, "");
                _state.Set(StateKeys.Input.Prompt, "");
                _state.Set(StateKeys.Input.DslStore, "");
                _state.Set<object>(StateKeys.Input.Options, Array.Empty<string>());
                break;

            case CallScreenCommand cs:
                if (cs.Params != null)
                    _state.Set(StateKeys.Screen.Params, cs.Params);
                else
                    _state.Set<object?>(StateKeys.Screen.Params, null);
                _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.CallScreen);
                _state.Set<object?>(StateKeys.Screen.Result, null);
                _ = _pipeline.SendAsync(new NavigateCommand { Path = cs.SceneName });
                await WaitForScreenResult(ct);
                _state.Set(StateKeys.Dsl.WaitingType, "");
                break;

            default:
                // 非交互命令——直接发送到管道
                _ = _pipeline.SendAsync(cmd);
                break;
        }
    }

    // ========== 异步等待方法 ==========

    /// <summary>交互等待超时上限（秒）——防止状态键 bug 导致永久挂起</summary>
    private const double InteractionTimeoutSeconds = 300;

    private async Task WaitForDialogComplete(CancellationToken ct)
    {
        // Fast path
        if (_state.Get<bool>(StateKeys.Dialog.Complete))
        {
            _state.Set(StateKeys.Dialog.Complete, false);
            return;
        }

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<bool>(StateKeys.Dialog.Complete),
                TimeSpan.FromSeconds(InteractionTimeoutSeconds),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[DslExecutor] WaitForDialogComplete 超时(300s)，强制推进");
        }
        catch (OperationCanceledException)
        {
            // ct 被取消（如 WaitCommand 中 Task.WhenAny 后 waitCts.Cancel）——正常返回，避免未观察任务异常
            return;
        }

        _state.Set(StateKeys.Dialog.Complete, false);
    }

    private async Task WaitForTransitionComplete(CancellationToken ct)
    {
        // 阶段 1：等待过渡激活（5 秒超时）
        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<bool>(StateKeys.Transition.Active),
                TimeSpan.FromSeconds(5),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[DslExecutor] WaitForTransitionComplete: 等待激活超时(5s)，跳过等待");
            return;
        }

        if (ct.IsCancellationRequested) return;

        // 阶段 2：等待过渡完成（60 秒超时）
        try
        {
            await _waitService.WaitForAsync(
                () => !_state.Get<bool>(StateKeys.Transition.Active),
                TimeSpan.FromSeconds(60),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[DslExecutor] WaitForTransitionComplete 超时(60s)，强制推进");
        }
    }

    private async Task<int> WaitForMenuSelection(CancellationToken ct)
    {
        // Fast path
        var selected = _state.Get<int>(StateKeys.Menu.Selected);
        if (selected >= 0)
        {
            _state.Set(StateKeys.Menu.Selected, -1);
            return selected;
        }

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<int>(StateKeys.Menu.Selected) >= 0,
                TimeSpan.FromSeconds(InteractionTimeoutSeconds),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[DslExecutor] WaitForMenuSelection 超时(300s)，返回 -1");
            return -1;
        }

        if (ct.IsCancellationRequested) return -1;

        var result = _state.Get<int>(StateKeys.Menu.Selected);
        _state.Set(StateKeys.Menu.Selected, -1);
        return result;
    }

    private async Task<string> WaitForInput(CancellationToken ct)
    {
        // Fast path
        var result = _state.Get<string?>(StateKeys.Input.Result);
        if (result != null)
        {
            _state.Set<object?>(StateKeys.Input.Result, null);
            return result;
        }

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<string?>(StateKeys.Input.Result) != null,
                TimeSpan.FromSeconds(InteractionTimeoutSeconds),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[DslExecutor] WaitForInput 超时(300s)，返回空字符串");
            return "";
        }

        if (ct.IsCancellationRequested) return "";

        var inputResult = _state.Get<string?>(StateKeys.Input.Result);
        _state.Set<object?>(StateKeys.Input.Result, null);
        return inputResult ?? "";
    }

    private async Task WaitForScreenResult(CancellationToken ct)
    {
        // Fast path
        if (_state.Get<string?>(StateKeys.Screen.Result) != null)
            return;

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<string?>(StateKeys.Screen.Result) != null,
                TimeSpan.FromSeconds(InteractionTimeoutSeconds),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[DslExecutor] WaitForScreenResult 超时(300s)，强制推进");
        }
    }

    /// <summary>
    /// 样式合并——如果元素有 class 属性，查找 __style_{class} 并合并属性
    /// </summary>
    private void ApplyStyleIfExists(UIElementEntity element)
    {
        if (!element.Properties.TryGetValue("class", out var classVal) || classVal == null) return;
        var styleName = classVal.ToString();
        if (string.IsNullOrEmpty(styleName)) return;

        var style = _state.Get<Dictionary<string, object?>>(StateKeys.Styles.Prefix + styleName);
        if (style == null) return;

        foreach (var (key, value) in style)
        {
            if (!element.Properties.ContainsKey(key) && value != null)
                element.Properties[key] = value;
        }
    }

    // ========== 统一线性回溯时间线（Phase 16/16.1）==========

    /// <inheritdoc/>
    public bool CanRollback()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        return checkpoints != null && checkpoints.Count > 0 && currentPos > 0;
    }

    /// <inheritdoc/>
    public bool CanRollforward()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        return checkpoints != null && currentPos >= 0 && currentPos < checkpoints.Count;
    }

    /// <inheritdoc/>
    public bool RollbackTo(int targetPos)
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        if (checkpoints == null || targetPos < 0 || targetPos >= checkpoints.Count) return false;

        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        if (targetPos >= currentPos) return false;

        RestoreAndRestart(checkpoints[targetPos], targetPos, checkpoints.Count);
        return true;
    }

    /// <inheritdoc/>
    public bool Rollback()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        if (checkpoints == null || currentPos <= 0) return false;

        var targetPos = currentPos - 1;
        RestoreAndRestart(checkpoints[targetPos], targetPos, checkpoints.Count);
        return true;
    }

    /// <inheritdoc/>
    public bool Rollforward()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        if (checkpoints == null || currentPos < 0 || currentPos >= checkpoints.Count) return false;

        var targetPos = currentPos + 1;

        if (targetPos >= checkpoints.Count)
        {
            RestoreAndRestart(checkpoints[^1], checkpoints.Count, checkpoints.Count);
            return true;
        }

        RestoreAndRestart(checkpoints[targetPos], targetPos, checkpoints.Count);
        return true;
    }

    // ========== 检查点内部实现 ==========

    private void AdvanceRollbackFrontier()
    {
        var cps = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        if (cps != null && cps.Count > 0)
            _state.Set(StateKeys.Rollback.CurrentIndex, cps.Count);
    }

    private void RestoreAndRestart(RollbackCheckpoint cp, int targetPos, int totalCheckpoints)
    {
        // 递增 C# 场景回放代次——使过期的 C# 场景 Runner 中的 SayAsync 等阻塞调用提前返回
        var gen = _state.Get<int>(StateKeys.Dsl.CSharpReplayGeneration) + 1;
        _state.Set(StateKeys.Dsl.CSharpReplayGeneration, gen);

        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
        cts?.Dispose();
        Interlocked.Exchange(ref _runTask, null);

        RestoreCheckpointState(cp);

        // Phase 41: 回溯时关闭 Skip/Auto 模式——这些键已从快照中排除（s_rollbackKeys），
        // 不被 RestoreCheckpointState 恢复也不被删除，需在此显式关闭。
        // 回溯 = 浏览历史，不应继续自动跳过/推进。
        _state.Set(StateKeys.Playback.SkipActive, false);
        _state.Set(StateKeys.Playback.AutoActive, false);
        _state.Set(StateKeys.Playback.AutoTimer, 0.0);

        // 解除可能正在阻塞的 C# 场景 Runner 中的 PollUntilTrue / TransitionAsync 轮询
        // RestoreCheckpointState 恢复了快照中的值，这里覆盖为完成态以快速唤醒过期 Runner
        _state.Set(StateKeys.Dialog.WaitingSayComplete, true);
        _state.Set(StateKeys.Transition.Active, false);

        _state.Set(StateKeys.Dsl.CurrentIndex, cp.CommandIndex);
        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Dsl.Executing, true);
        _state.Set(StateKeys.Scene.Dirty, true);

        _state.Set(StateKeys.Rollback.CurrentIndex, targetPos);
        _state.Set(StateKeys.Rollback.IsActive, targetPos < totalCheckpoints - 1);
        _state.Set(StateKeys.Rollback.IsReplay, true);

        if (cp.CommandIndex < 0 && cp.InteractionType == "csharp_scene")
        {
            _state.Set(StateKeys.Dsl.Executing, false);
            _state.Set(StateKeys.Scene.CurrentName, cp.SceneName);
            _state.Set(StateKeys.Scene.Dirty, true);
            if (OnCSharpSceneReplay != null)
            {
                _ = OnCSharpSceneReplay.Invoke(cp.SceneName);
            }
            else
            {
                _state.Set(StateKeys.Rollback.IsReplay, false);
                _state.Set(StateKeys.Rollback.IsActive, false);
            }
        }
        else
        {
            BeginRunAsync();
        }
    }

    /// <summary>
    /// 创建场景级检查点（C# StoryScript 场景入口调用）
    /// </summary>
    public void CreateSceneCheckpoint(string sceneName)
    {
        // Phase 60: 小说世界模式禁用 C# 场景检查点
        if (_options.EnableTimeSystem) return;

        var currentType = _state.Get<int>(StateKeys.Scene.CurrentType);
        if ((SceneType)currentType != SceneType.Game) return;

        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints) ?? new List<RollbackCheckpoint>();
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);

        if (currentPos >= 0 && currentPos + 1 < checkpoints.Count)
            checkpoints.RemoveRange(currentPos + 1, checkpoints.Count - currentPos - 1);

        var snapshot = new Dictionary<string, object?>();
        foreach (var (k, v) in _state.GetSnapshot())
        {
            if (s_rollbackKeys.Contains(k)) continue;
            snapshot[k] = DeepCopyMutable(k, v);
        }

        checkpoints.Add(new RollbackCheckpoint
        {
            CommandIndex = -1,
            SceneName = sceneName,
            InteractionType = "csharp_scene",
            StateSnapshot = snapshot
        });

        var maxCps = _options.MaxRollbackCheckpoints;
        while (checkpoints.Count > maxCps) checkpoints.RemoveAt(0);

        _state.Set(StateKeys.Rollback.Checkpoints, checkpoints);
        _state.Set(StateKeys.Rollback.CurrentIndex, checkpoints.Count);
        _state.Set(StateKeys.Rollback.IsActive, false);
        _state.Set(StateKeys.Rollback.IsReplay, false);
    }

    private void CreateCheckpoint(int commandIndex, string interactionType = StateKeys.Dsl.WaitingTypes.Dialog)
    {
        // Phase 60: 小说世界模式禁用逐句回溯——时间锚点存档是唯一的"历史"
        if (_options.EnableTimeSystem) return;

        // Phase 24: block_rollback——如果当前命令索引 >= 阻止标记，跳过检查点创建
        var blockedUntil = _state.Get<int>(StateKeys.Rollback.BlockedUntil);
        if (blockedUntil >= 0 && commandIndex >= blockedUntil)
            return;

        var currentType = _state.Get<int>(StateKeys.Scene.CurrentType);
        if ((SceneType)currentType != SceneType.Game)
            return;

        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints) ?? new List<RollbackCheckpoint>();
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);

        if (currentPos >= 0 && currentPos + 1 < checkpoints.Count)
        {
            checkpoints.RemoveRange(currentPos + 1, checkpoints.Count - currentPos - 1);
        }

        var snapshot = new Dictionary<string, object?>();
        foreach (var (k, v) in _state.GetSnapshot())
        {
            if (s_rollbackKeys.Contains(k))
                continue;
            snapshot[k] = DeepCopyMutable(k, v);
        }

        var sceneName = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";

        checkpoints.Add(new RollbackCheckpoint
        {
            CommandIndex = commandIndex,
            SceneName = sceneName,
            InteractionType = interactionType,
            StateSnapshot = snapshot
        });

        var maxCps = _options.MaxRollbackCheckpoints;
        while (checkpoints.Count > maxCps)
            checkpoints.RemoveAt(0);

        _state.Set(StateKeys.Rollback.Checkpoints, checkpoints);
        _state.Set(StateKeys.Rollback.CurrentIndex, checkpoints.Count - 1);
        _state.Set(StateKeys.Rollback.IsActive, false);
        _state.Set(StateKeys.Rollback.IsReplay, false);

        if (interactionType == StateKeys.Dsl.WaitingTypes.Dialog)
        {
            var seenKey = $"{sceneName}:{commandIndex}";
            var seen = _state.Get<HashSet<string>>(StateKeys.Playback.SeenSayIndices) ?? [];
            seen.Add(seenKey);
            _state.Set(StateKeys.Playback.SeenSayIndices, seen);
        }
    }

    private void RestoreCheckpointState(RollbackCheckpoint cp)
    {
        foreach (var (k, _) in _state.GetSnapshot())
        {
            if (!s_rollbackKeys.Contains(k))
                _state.Remove(k);
        }

        foreach (var (k, v) in cp.StateSnapshot)
            _state.Set(k, DeepCopyMutable(k, v));
    }

    private static object? DeepCopyMutable(string key, object? value)
    {
        switch (value)
        {
            case List<UIElementEntity> els:
                // 深拷贝——UIElementEntity 是 class，Properties/Children 可变
                // 浅拷贝（new List(els)）会导致快照和运行时共享同一元素引用，修改 Properties 会污染快照
                var elCopy = new List<UIElementEntity>(els.Count);
                foreach (var el in els)
                    elCopy.Add(DeepCopyElement(el));
                return elCopy;
            case List<RollbackCheckpoint> rps:
                return new List<RollbackCheckpoint>(rps);
            case List<DialogHistoryEntry> dhes:
                return new List<DialogHistoryEntry>(dhes);
            case List<int> ints:
                return new List<int>(ints);
            case List<string> strs:
                return new List<string>(strs);
            case List<GalleryEntry> gals:
                // 深拷贝——GalleryEntry 是 class，可能被运行时修改
                return gals.Select(g => new GalleryEntry { Id = g.Id, ImagePath = g.ImagePath, Title = g.Title, SceneName = g.SceneName, UnlockedAt = g.UnlockedAt }).ToList();
            case List<AchievementEntry> achs:
                // 深拷贝——AchievementEntry 是 class，防止快照与运行时共享引用
                return achs.Select(a => new AchievementEntry { Id = a.Id, Name = a.Name, UnlockedAt = a.UnlockedAt }).ToList();
            case List<ChapterEntry> chaps:
                // 深拷贝——ChapterEntry 是 class，Unlocked/UnlockedAt 可被 ChapterUnlockHandler 修改
                return chaps.Select(c => new ChapterEntry { Id = c.Id, Name = c.Name, Unlocked = c.Unlocked, UnlockedAt = c.UnlockedAt }).ToList();
            case List<DebugLogEntry> logs:
                return new List<DebugLogEntry>(logs);
            case List<object?> objs:
                // 深拷贝——元素可能为可变对象（Dictionary/List），递归拷贝防止快照与运行时共享引用
                var objCopy = new List<object?>(objs.Count);
                foreach (var o in objs)
                    objCopy.Add(DeepCopyMutable("", o));
                return objCopy;
            case HashSet<string> hs:
                return new HashSet<string>(hs, hs.Comparer);
            case Dictionary<string, object?> dict:
                var copy = new Dictionary<string, object?>(dict.Count, dict.Comparer as IEqualityComparer<string> ?? StringComparer.Ordinal);
                foreach (var (k, v) in dict)
                    copy[k] = DeepCopyMutable(k, v);
                return copy;
            default:
                return value;
        }
    }

    /// <summary>
    /// 深拷贝 UIElementEntity——复制 Properties 字典和递归复制 Children
    /// <para>防止回溯快照与运行时共享元素引用导致状态污染。</para>
    /// </summary>
    private static UIElementEntity DeepCopyElement(UIElementEntity src)
    {
        var clone = new UIElementEntity
        {
            Id = src.Id,
            ElementType = src.ElementType,
            InCustom = src.InCustom,
            CustomElement = src.CustomElement,
            Order = src.Order,
            Command = src.Command,
            CommandValue = src.CommandValue,
            Properties = new Dictionary<string, object>(src.Properties.Count, src.Properties.Comparer as IEqualityComparer<string> ?? StringComparer.Ordinal)
        };
        foreach (var (pk, pv) in src.Properties)
            clone.Properties[pk] = pv;
        foreach (var child in src.Children)
            clone.Children.Add(DeepCopyElement(child));
        return clone;
    }

    /// <inheritdoc/>
    public void ClearCheckpoints()
    {
        _state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint>());
        _state.Set(StateKeys.Rollback.CurrentIndex, -1);
        _state.Set(StateKeys.Rollback.IsActive, false);
        _state.Set(StateKeys.Rollback.IsReplay, false);
    }
}
