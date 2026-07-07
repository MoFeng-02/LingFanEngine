using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// DSL 执行器——异步优先的命令执行器
/// <para>RunAsync 为主执行循环，遇到交互命令（say/menu/input/wait）时
/// 使用 async/await 天然等待，无需帧轮询状态标记。</para>
/// <para>所有状态保存在状态容器中，不维护私有内部状态（除 CancellationTokenSource）。</para>
/// <para>支持 Say 级回溯：每个 ShowDialogCommand 创建检查点，
/// 回溯时取消当前 RunAsync、恢复检查点状态、重启 RunAsync。</para>
/// </summary>
public class DslExecutor : IDslExecutor
{
    private readonly IStateContainer _state;
    private readonly ICommandPipeline _pipeline;
    private readonly LingFanEngineOptions _options;
    private IStoryRegistry? _storyRegistry;

    /// <summary>异步执行取消令牌</summary>
    private CancellationTokenSource? _cts;
    /// <summary>当前运行中的执行任务</summary>
    private Task? _runTask;

    /// <summary>回溯自身相关的键，快照时排除</summary>
    private static readonly HashSet<string> s_rollbackKeys = new()
    {
        StateKeys.Rollback.Checkpoints,
        StateKeys.Rollback.CurrentIndex,
        StateKeys.Rollback.IsActive,
        StateKeys.Rollback.IsReplay,
        StateKeys.Playback.SeenSayIndices
    };

    public DslExecutor(IStateContainer state, ICommandPipeline pipeline, LingFanEngineOptions? options = null)
    {
        _state = state;
        _pipeline = pipeline;
        _options = options ?? new LingFanEngineOptions();
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
        _cts?.Cancel();
        _cts = null;
        _runTask = null;
        _state.Set(StateKeys.Dsl.Executing, false);
        _state.Set(StateKeys.Dsl.WaitingType, "");
    }

    /// <summary>
    /// 启动 RunAsync 任务（fire-and-forget）
    /// </summary>
    private void BeginRunAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
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
                var currentIndex = _state.Get<int>(StateKeys.Dsl.CurrentIndex);

                if (currentIndex >= commands.Count)
                {
                    // 命令列表耗尽——清除对话状态，让 SceneView 隐藏对话框
                    _state.Set(StateKeys.Dsl.Executing, false);
                    _state.Set(StateKeys.Dialog.Text, "");
                    break;
                }

                var cmd = commands[currentIndex];

                switch (cmd)
                {
                    // ========== 交互命令（async/await 等待）==========

                    case ShowDialogCommand dialog:
                    {
                        // 创建回溯检查点（回溯重展示时不创建新检查点）
                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Dialog);

                        // 投递到管道（ShowDialogHandler 设置对话文本/说话者等）
                        _ = _pipeline.SendAsync(cmd);
                        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Dialog);

                        // 异步等待用户点击继续
                        await WaitForDialogComplete(ct);
                        if (ct.IsCancellationRequested) return;

                        _state.Set(StateKeys.Dsl.WaitingType, "");

                        // 回溯模式：点击继续 = 前进到下一个检查点
                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                            // Rollforward 失败：回退到正常推进
                        }

                        // 到达前沿或不在回溯模式：正常推进
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case WaitCommand wait:
                    {
                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Wait);

                        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Wait);
                        _state.Set(StateKeys.Dsl.WaitUntil, Environment.TickCount64 / 1000.0 + wait.Seconds);
                        _state.Set(StateKeys.Dsl.WaitDuration, wait.Seconds);

                        // 异步等待指定时长
                        try { await Task.Delay(TimeSpan.FromSeconds(wait.Seconds), ct); }
                        catch (OperationCanceledException) { return; }

                        if (ct.IsCancellationRequested) return;
                        _state.Set(StateKeys.Dsl.WaitingType, "");

                        // 回溯模式处理
                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                            // Rollforward 失败：回退到正常推进
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case MenuCommand menu:
                    {
                        // 设置菜单状态供 SceneView 渲染
                        _state.Set(StateKeys.Menu.Prompt, menu.Prompt);
                        _state.Set<object>(StateKeys.Menu.Options, menu.Options.Select(o => o.Text).ToArray());
                        _state.Set(StateKeys.Menu.Selected, -1);
                        // 存储目标 label 列表供跳转
                        _state.Set(StateKeys.Menu.DslTargets, string.Join(",", menu.Options.Select(o => o.TargetLabel)));
                        _state.Set(StateKeys.Menu.DslTexts, string.Join(",", menu.Options.Select(o => o.Text)));

                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Menu);

                        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Menu);

                        // 异步等待用户选择
                        var selectedIdx = await WaitForMenuSelection(ct);
                        if (ct.IsCancellationRequested) return;

                        // 清除菜单状态
                        _state.Set(StateKeys.Dsl.WaitingType, "");
                        _state.Set(StateKeys.Menu.Prompt, "");
                        _state.Set<object>(StateKeys.Menu.Options, Array.Empty<string>());
                        _state.Set(StateKeys.Menu.Selected, -1);
                        _state.Set(StateKeys.Menu.DslTargets, "");
                        _state.Set(StateKeys.Menu.DslTexts, "");

                        // 回溯模式处理
                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                            // Rollforward 失败：回退到正常推进
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);

                        // 跳转到选中选项的 label
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
                        // 设置输入状态供 SceneView 渲染
                        _state.Set(StateKeys.Input.Prompt, input.Prompt);
                        _state.Set(StateKeys.Input.DslStore, input.StoreKey);
                        _state.Set<object>(StateKeys.Input.Options, input.Options ?? Array.Empty<string>());
                        _state.Set<object?>(StateKeys.Input.Result, null);

                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Input);

                        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Input);

                        // 异步等待用户提交
                        var inputValue = await WaitForInput(ct);
                        if (ct.IsCancellationRequested) return;

                        // 清除输入状态
                        _state.Set(StateKeys.Dsl.WaitingType, "");
                        _state.Set(StateKeys.Input.Prompt, "");
                        _state.Set(StateKeys.Input.DslStore, "");
                        _state.Set<object>(StateKeys.Input.Options, Array.Empty<string>());

                        // 回溯模式处理
                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            if (Rollforward())
                                return;
                            // Rollforward 失败：回退到正常推进
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);

                        // 存储输入值
                        if (!string.IsNullOrEmpty(input.StoreKey))
                            _state.Set(input.StoreKey, inputValue);

                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case EndCommand:
                        // 块结束哨兵——停止执行
                        // 清除对话状态，让 SceneView 检测到 Dialog.Text 变空 → 调用 Hide()
                        _state.Set(StateKeys.Dsl.Executing, false);
                        _state.Set(StateKeys.Dialog.Text, "");
                        return;

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
                        // 同步执行（立即改 state），不经过管道
                        // 否则后面的 if 判断会读到旧值
                        if (sv.IsDefine && _state.ContainsKey(sv.Key))
                        {
                            // define ... once：跳过
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
                        // 过渡动画——投递到管道启动，然后等待动画完成
                        _ = _pipeline.SendAsync(cmd);
                        await WaitForTransitionComplete(ct);
                        if (ct.IsCancellationRequested) return;
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;

                    case ShowElementCommand se:
                        // 场景元素——同步追加到 Scene.Elements + 标记 Dirty
                        // 非阻塞：立即执行，后续的 say/transition 等命令自然实现"等待后才揭示后续元素"
                        var elements = _state.Get<List<UIElementEntity>>(StateKeys.Scene.Elements) ?? new List<UIElementEntity>();
                        elements.Add(se.Element);
                        _state.Set(StateKeys.Scene.Elements, elements);
                        _state.Set(StateKeys.Scene.Dirty, true);
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;

                    default:
                        // 其他命令——fire-and-forget 到管道
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

    // ========== 异步等待方法 ==========

    /// <summary>
    /// 等待对话完成（用户点击继续）
    /// <para>轮询 Dialog.Complete 状态，16ms 间隔。</para>
    /// <para>PlaybackService 的 Skip/Auto 模式也会设置此标记。</para>
    /// </summary>
    private async Task WaitForDialogComplete(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_state.Get<bool>(StateKeys.Dialog.Complete))
            {
                _state.Set(StateKeys.Dialog.Complete, false);
                return;
            }
            try { await Task.Delay(16, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// 等待过渡动画完成
    /// <para>轮询 Transition.Active 状态，16ms 间隔。</para>
    /// <para>TransitionEngine 每帧推进 Progress，完成后设 Active=false。</para>
    /// </summary>
    private async Task WaitForTransitionComplete(CancellationToken ct)
    {
        // 先等一帧确保 TransitionHandler 已设置 Active=true
        try { await Task.Delay(16, ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            if (!_state.Get<bool>(StateKeys.Transition.Active))
                return;
            try { await Task.Delay(16, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// 等待菜单选择
    /// <para>轮询 Menu.Selected 状态（SceneView 设置），返回选中索引。</para>
    /// </summary>
    private async Task<int> WaitForMenuSelection(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var selected = _state.Get<int>(StateKeys.Menu.Selected);
            if (selected >= 0)
            {
                _state.Set(StateKeys.Menu.Selected, -1);
                return selected;
            }
            try { await Task.Delay(16, ct); }
            catch (OperationCanceledException) { return -1; }
        }
        return -1;
    }

    /// <summary>
    /// 等待用户输入
    /// <para>轮询 Input.Result 状态（SceneView 设置），返回输入文本。</para>
    /// </summary>
    private async Task<string> WaitForInput(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var result = _state.Get<string?>(StateKeys.Input.Result);
            if (result != null)
            {
                _state.Set<object?>(StateKeys.Input.Result, null);
                return result;
            }
            try { await Task.Delay(16, ct); }
            catch (OperationCanceledException) { return ""; }
        }
        return "";
    }

    // ========== Say 级回溯 ==========

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
        return checkpoints != null && currentPos >= 0 && currentPos < checkpoints.Count - 1;
    }

    /// <inheritdoc/>
    public bool RollbackTo(int targetPos)
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        if (checkpoints == null || targetPos < 0 || targetPos >= checkpoints.Count) return false;

        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        if (targetPos >= currentPos) return false;

        RestoreAndRestart(checkpoints[targetPos], targetPos, checkpoints.Count);
        System.Diagnostics.Debug.WriteLine(
            $"[DslExecutor] RollbackTo -> pos={targetPos}, cmdIdx={checkpoints[targetPos].CommandIndex}, type={checkpoints[targetPos].InteractionType}");
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
        System.Diagnostics.Debug.WriteLine(
            $"[DslExecutor] Rollback -> pos={targetPos}, cmdIdx={checkpoints[targetPos].CommandIndex}, type={checkpoints[targetPos].InteractionType}");
        return true;
    }

    /// <inheritdoc/>
    public bool Rollforward()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        if (checkpoints == null || currentPos < 0 || currentPos >= checkpoints.Count - 1) return false;

        var targetPos = currentPos + 1;
        RestoreAndRestart(checkpoints[targetPos], targetPos, checkpoints.Count);
        System.Diagnostics.Debug.WriteLine(
            $"[DslExecutor] Rollforward -> pos={targetPos}, cmdIdx={checkpoints[targetPos].CommandIndex}, type={checkpoints[targetPos].InteractionType}");
        return true;
    }

    // ========== 检查点内部实现 ==========

    /// <summary>
    /// 恢复检查点状态并重启 RunAsync
    /// <para>1. 取消当前 RunAsync（CancellationToken）</para>
    /// <para>2. 恢复检查点状态快照</para>
    /// <para>3. 设置 CurrentIndex 为检查点的命令索引</para>
    /// <para>4. 重启 RunAsync——它会重新执行该命令并 await</para>
    /// </summary>
    private void RestoreAndRestart(RollbackCheckpoint cp, int targetPos, int totalCheckpoints)
    {
        // 1. 取消当前 RunAsync
        _cts?.Cancel();
        _cts = null;
        _runTask = null;

        // 2. 恢复状态快照
        RestoreCheckpointState(cp);

        // 3. 设置 DSL 状态
        _state.Set(StateKeys.Dsl.CurrentIndex, cp.CommandIndex);
        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Dsl.Executing, true);

        // 4. 更新回溯位置
        _state.Set(StateKeys.Rollback.CurrentIndex, targetPos);
        _state.Set(StateKeys.Rollback.IsActive, targetPos < totalCheckpoints - 1);
        _state.Set(StateKeys.Rollback.IsReplay, true);

        // 5. 重启 RunAsync——它会从 cp.CommandIndex 重新执行
        // 对于 dialog：重新 SendAsync（显示对话）+ await
        // 对于 wait：重新 await Task.Delay
        // 对于 menu：重新设置菜单状态 + await
        // 对于 input：重新设置输入状态 + await
        BeginRunAsync();
    }

    /// <summary>
    /// 在执行交互命令前创建检查点
    /// </summary>
    private void CreateCheckpoint(int commandIndex, string interactionType = StateKeys.Dsl.WaitingTypes.Dialog)
    {
        // Menu/UI 场景不创建回退检查点
        var currentType = _state.Get<int>(StateKeys.Scene.CurrentType);
        if ((SceneType)currentType != SceneType.Game)
            return;

        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints) ?? new List<RollbackCheckpoint>();
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);

        // 如果在回退状态下有新交互，截断未来检查点（新时间线）
        if (currentPos >= 0 && currentPos + 1 < checkpoints.Count)
        {
            checkpoints.RemoveRange(currentPos + 1, checkpoints.Count - currentPos - 1);
        }

        // 快照当前状态（排除回溯自身键）
        var snapshot = new Dictionary<string, object?>();
        foreach (var (k, v) in _state.GetSnapshot())
        {
            if (!s_rollbackKeys.Contains(k))
                snapshot[k] = v;
        }

        var sceneName = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";

        checkpoints.Add(new RollbackCheckpoint
        {
            CommandIndex = commandIndex,
            SceneName = sceneName,
            InteractionType = interactionType,
            StateSnapshot = snapshot
        });

        // 超出上限时丢弃最旧的
        var maxCps = _options.MaxRollbackCheckpoints;
        while (checkpoints.Count > maxCps)
            checkpoints.RemoveAt(0);

        _state.Set(StateKeys.Rollback.Checkpoints, checkpoints);
        _state.Set(StateKeys.Rollback.CurrentIndex, checkpoints.Count - 1);
        _state.Set(StateKeys.Rollback.IsActive, false);
        _state.Set(StateKeys.Rollback.IsReplay, false);

        // 记录已读 Say
        if (interactionType == StateKeys.Dsl.WaitingTypes.Dialog)
        {
            var seenKey = $"{sceneName}:{commandIndex}";
            var seen = _state.Get<HashSet<string>>(StateKeys.Playback.SeenSayIndices) ?? [];
            seen.Add(seenKey);
            _state.Set(StateKeys.Playback.SeenSayIndices, seen);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[DslExecutor] Checkpoint created: pos={checkpoints.Count - 1}, cmdIdx={commandIndex}, type={interactionType}");
    }

    /// <summary>恢复检查点状态：先清除当前状态，再写入快照</summary>
    private void RestoreCheckpointState(RollbackCheckpoint cp)
    {
        // 1. 清除当前状态（排除回溯自身键）
        foreach (var (k, _) in _state.GetSnapshot())
        {
            if (!s_rollbackKeys.Contains(k))
                _state.Remove(k);
        }

        // 2. 写入快照
        foreach (var (k, v) in cp.StateSnapshot)
            _state.Set(k, v);
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
