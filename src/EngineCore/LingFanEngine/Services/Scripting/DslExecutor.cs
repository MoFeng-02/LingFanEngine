using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// DSL 执行器——纯状态驱动的命令执行器
/// <para>所有状态保存在状态容器中，不维护私有内部状态。</para>
/// <para>外部调用 Step() 推进执行，内部用循环避免递归。</para>
/// <para>支持 Say 级回溯：每个 ShowDialogCommand 创建检查点，</para>
/// <para>回溯时恢复检查点状态并重新展示对话。</para>
/// </summary>
public class DslExecutor
{
    private readonly IStateContainer _state;
    private readonly ICommandPipeline _pipeline;
    private StoryRegistry? _storyRegistry;

    /// <summary>最大检查点数量（超出时丢弃最旧的）</summary>
    private const int MaxCheckpoints = 100;

    /// <summary>回溯自身相关的键，快照时排除</summary>
    private static readonly HashSet<string> s_rollbackKeys = new()
    {
        StateKeys.Rollback.Checkpoints,
        StateKeys.Rollback.CurrentIndex,
        StateKeys.Rollback.IsActive
    };

    public DslExecutor(IStateContainer state, ICommandPipeline pipeline)
    {
        _state = state;
        _pipeline = pipeline;
    }

    /// <summary>
    /// 注册 StoryRegistry（用于自动解析 label 所在文件）
    /// </summary>
    public void SetStoryRegistry(StoryRegistry registry)
    {
        _storyRegistry = registry;
    }

    public void LoadCommands(IReadOnlyList<ICommand> commands, IReadOnlyDictionary<string, int>? labels = null)
    {
        _state.Set(StateKeys.Dsl.Commands, commands.ToList());
        _state.Set(StateKeys.Dsl.Labels, labels ?? new Dictionary<string, int>());
        _state.Set(StateKeys.Dsl.CurrentIndex, 0);
        _state.Set(StateKeys.Dsl.Executing, false);
        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Dsl.WaitingValue, (object?)null);
        _state.Set(StateKeys.Dsl.TotalCommands, commands.Count);

        // 新故事加载时清除回溯检查点
        ClearCheckpoints();
    }

    /// <summary>
    /// 从头开始执行（从索引 0 启动，不跳 label）
    /// </summary>
    public void Start()
    {
        _state.Set(StateKeys.Dsl.CurrentIndex, 0);
        _state.Set(StateKeys.Dsl.Executing, true);
        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Dsl.WaitingValue, (object?)null);
    }

    public void StartFromLabel(string label)
    {
        var labels = _state.Get<IReadOnlyDictionary<string, int>>(StateKeys.Dsl.Labels) ??
                     _state.Get<Dictionary<string, int>>(StateKeys.Dsl.Labels);
        var commands = _state.Get<List<ICommand>>(StateKeys.Dsl.Commands);

        // 当前已加载的 labels 中有该 label
        if (labels != null && commands != null && labels.TryGetValue(label, out var idx))
        {
            _state.Set(StateKeys.Dsl.CurrentIndex, idx);
            _state.Set(StateKeys.Dsl.Executing, true);
            _state.Set(StateKeys.Dsl.WaitingType, "");
            _state.Set(StateKeys.Dsl.WaitingValue, (object?)null);
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
                    LoadCommands(cmds, lbls);
                    _state.Set(StateKeys.Dsl.CurrentIndex, idx2);
                    _state.Set(StateKeys.Dsl.Executing, true);
                    _state.Set(StateKeys.Dsl.WaitingType, "");
                    _state.Set(StateKeys.Dsl.WaitingValue, (object?)null);
                    System.Diagnostics.Debug.WriteLine($"[DslExecutor] 自动加载 label [{label}] 来自 {filePath}");
                }
            }
        }
    }

    /// <summary>
    /// 单步推进——由 GameLoop 每帧调用
    /// 内部用 while 循环替代递归，最多执行 200 条命令后让出控制权
    /// </summary>
    public bool Step()
    {
        var commands = _state.Get<List<ICommand>>(StateKeys.Dsl.Commands);
        var executing = _state.Get<bool>(StateKeys.Dsl.Executing);
        if (!executing || commands == null) return false;

        int budget = 200;
        while (budget-- > 0)
        {
            var currentIndex = _state.Get<int>(StateKeys.Dsl.CurrentIndex);
            var waitingType = _state.Get<string>(StateKeys.Dsl.WaitingType);

            // 检查阻塞状态（wait/menu/input/dialog 需要等待外部事件）
            if (!string.IsNullOrEmpty(waitingType))
            {
                if (waitingType == "wait")
                {
                    var waitUntil = _state.Get<double>(StateKeys.Dsl.WaitUntil);
                    if (Environment.TickCount64 / 1000.0 >= waitUntil)
                    {
                        _state.Set(StateKeys.Dsl.WaitingType, "");
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        continue;
                    }
                    return false;
                }
                if (waitingType == "dialog")
                {
                    // 检查用户是否点击了继续（SceneView 设 __dialog_complete）
                    if (_state.Get<bool>(StateKeys.Dialog.Complete))
                    {
                        _state.Set(StateKeys.Dialog.Complete, false);

                        // 回溯模式：点击继续 = 前进到下一个检查点
                        var isRollback = _state.Get<bool>(StateKeys.Rollback.IsActive);
                        if (isRollback && CanRollforward())
                        {
                            Rollforward();
                            return false;
                        }

                        // 到达前沿或不在回溯模式：正常推进
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Dsl.WaitingType, "");
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        continue;
                    }
                    return false;
                }
                return false; // menu/input
            }

            if (currentIndex >= commands.Count)
            {
                _state.Set(StateKeys.Dsl.Executing, false);
                return false;
            }

            var cmd = commands[currentIndex];

            switch (cmd)
            {
                case CallCommand call:
                    // 保存返回位置（当前索引 + 1）
                    var callStack = _state.Get<List<int>>(StateKeys.CallStack.Stack) ?? new List<int>();
                    callStack.Add(currentIndex + 1);
                    _state.Set(StateKeys.CallStack.Stack, callStack);
                    // 跳转到目标 label
                    var labels = _state.Get<Dictionary<string, int>>(StateKeys.Dsl.Labels);
                    if (labels != null && labels.TryGetValue(call.TargetLabel, out var callIdx))
                        _state.Set(StateKeys.Dsl.CurrentIndex, callIdx);
                    else
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                    continue;

                case ReturnCommand:
                    var retStack = _state.Get<List<int>>(StateKeys.CallStack.Stack);
                    if (retStack != null && retStack.Count > 0)
                    {
                        var retIdx = retStack[^1];
                        retStack.RemoveAt(retStack.Count - 1);
                        _state.Set(StateKeys.Dsl.CurrentIndex, retIdx);
                    }
                    else
                    {
                        // 没有 call 栈时，return 相当于 stop
                        _state.Set(StateKeys.Dsl.Executing, false);
                    }
                    continue;

                case JumpCommand jmp:
                    if (jmp.TargetIndex >= 0 && jmp.TargetIndex < commands.Count)
                        _state.Set(StateKeys.Dsl.CurrentIndex, jmp.TargetIndex);
                    else
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                    continue;

                case BranchCommand br:
                    if (br.Condition == null && br.SkipCount == 0 && !br.HasMatched)
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                    else if (br.HasMatched)
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + br.SkipCount + 1);
                    else if (br.Condition != null)
                    {
                        var conditionMet = DslExpressionEvaluator.EvaluateBool(br.Condition, _state);
                        _state.Set(StateKeys.Dsl.CurrentIndex,
                            currentIndex + (conditionMet ? 1 : br.SkipCount + 1));
                    }
                    else
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                    continue;

                case MenuCommand menu:
                    _state.Set(StateKeys.Dsl.WaitingType, "menu");
                    _state.Set(StateKeys.Menu.DslOptions, string.Join("|",
                        menu.Options.Select(o => $"{o.Text}->{o.TargetLabel}")));
                    _state.Set(StateKeys.Menu.DslTargets, string.Join(",", menu.Options.Select(o => o.TargetLabel)));
                    _state.Set(StateKeys.Menu.DslTexts, string.Join(",", menu.Options.Select(o => o.Text)));
                    return false;

                case EndCommand:
                    // 块结束哨兵——停止推进
                    _state.Set(StateKeys.Dsl.Executing, false);
                    return false;

                case WaitCommand wait:
                    _state.Set(StateKeys.Dsl.WaitingType, "wait");
                    _state.Set(StateKeys.Dsl.WaitUntil, Environment.TickCount64 / 1000.0 + wait.Seconds);
                    _state.Set(StateKeys.Dsl.WaitDuration, wait.Seconds);
                    return false;

                case InputCommand input:
                    _state.Set(StateKeys.Dsl.WaitingType, "input");
                    _state.Set(StateKeys.Input.DslPrompt, input.Prompt);
                    _state.Set(StateKeys.Input.DslStore, input.StoreKey);
                    _state.Set(StateKeys.Input.DslOptions, input.Options != null ? string.Join(",", input.Options) : "");
                    return false;

                case ShowDialogCommand dialog:
                    // 创建回溯检查点（在执行 Say 之前快照状态）
                    CreateCheckpoint(currentIndex);

                    // say 命令：投递到管道后暂停，等待用户点击继续
                    _pipeline.SendAsync(cmd);
                    _state.Set(StateKeys.Dsl.WaitingType, "dialog");
                    _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                    return false;

                default:
                    // 控制流命令（JumpCommand/BranchCommand）由 DslExecutor 已处理
                    // 不应再投递到管道；只投递业务命令（say/set/show/navigate 等）
                    if (cmd is JumpCommand or BranchCommand or NavToLabelCommand or MenuCommand
                        or CallCommand or ReturnCommand or AnimateCommand)
                    {
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        continue;
                    }

                    // SetVariableCommand：同步执行（立即改 _state），不经过管道
                    // 否则后面的 if 判断会读到旧值
                    if (cmd is SetVariableCommand sv)
                    {
                        if (sv.Value is DslExpressionPlaceholder placeholder)
                        {
                            var result = DslExpressionEvaluator.Evaluate(placeholder.Expression, _state);
                            _state.Set(sv.Key, result);
                        }
                        else
                        {
                            _state.Set(sv.Key, sv.Value);
                        }
                    }
                    else
                    {
                        _pipeline.SendAsync(cmd);
                    }
                    _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                    continue;
            }
        }
        return true;
    }

    public void SelectMenuOption(int optionIndex)
    {
        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Menu.DslOptions, "");
        _state.Set(StateKeys.Menu.DslTargets, "");
        _state.Set(StateKeys.Menu.DslTexts, "");
        var labels = _state.Get<IReadOnlyDictionary<string, int>>(StateKeys.Dsl.Labels) ??
                     _state.Get<Dictionary<string, int>>(StateKeys.Dsl.Labels);
        if (labels != null)
        {
            var targets = _state.Get<string>(StateKeys.Menu.DslTargets) ?? "";
            var targetList = targets.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (optionIndex >= 0 && optionIndex < targetList.Length)
            {
                var label = targetList[optionIndex];
                if (labels.TryGetValue(label, out var idx))
                {
                    _state.Set(StateKeys.Dsl.CurrentIndex, idx);
                    return;
                }
            }
        }
        _state.Set(StateKeys.Dsl.CurrentIndex, _state.Get<int>(StateKeys.Dsl.CurrentIndex) + 1);
    }

    public void SubmitInput(string value)
    {
        var storeKey = _state.Get<string>(StateKeys.Input.DslStore);
        if (storeKey != null)
            _state.Set(storeKey, value);
        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Input.DslPrompt, "");
        _state.Set(StateKeys.Input.DslStore, "");
        _state.Set(StateKeys.Input.DslOptions, "");
        _state.Set(StateKeys.Dsl.CurrentIndex, _state.Get<int>(StateKeys.Dsl.CurrentIndex) + 1);
    }

    // ========== Say 级回溯 ==========

    /// <summary>是否可以回溯（后退到上一个 Say）</summary>
    public bool CanRollback()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        return checkpoints != null && checkpoints.Count > 0 && currentPos > 0;
    }

    /// <summary>是否可以前进（前进到下一个 Say）</summary>
    public bool CanRollforward()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        return checkpoints != null && currentPos >= 0 && currentPos < checkpoints.Count - 1;
    }

    /// <summary>
    /// 后退到上一个 Say 检查点
    /// <para>恢复检查点状态快照 + 重新展示该 Say 对话。</para>
    /// <para>不推进 DSL 执行——用户点击"继续"时由 Step() 处理。</para>
    /// </summary>
    public bool Rollback()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        if (checkpoints == null || currentPos <= 0) return false;

        currentPos--;
        var cp = checkpoints[currentPos];

        // 恢复状态快照
        RestoreCheckpointState(cp);

        // 设置 DSL 状态：等待 dialog，索引指向该 Say 命令
        _state.Set(StateKeys.Dsl.CurrentIndex, cp.CommandIndex);
        _state.Set(StateKeys.Dsl.WaitingType, "dialog");
        _state.Set(StateKeys.Dsl.Executing, true);

        // 更新回溯位置
        _state.Set(StateKeys.Rollback.CurrentIndex, currentPos);
        _state.Set(StateKeys.Rollback.IsActive, currentPos < checkpoints.Count - 1);

        // 重新展示该 Say（投递到管道，ShowDialogHandler 会设置对话文本等）
        var commands = _state.Get<List<ICommand>>(StateKeys.Dsl.Commands);
        if (commands != null && cp.CommandIndex < commands.Count)
        {
            _pipeline.SendAsync(commands[cp.CommandIndex]);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[DslExecutor] Rollback -> pos={currentPos}, cmdIdx={cp.CommandIndex}");
        return true;
    }

    /// <summary>
    /// 前进到下一个 Say 检查点
    /// <para>恢复检查点状态快照 + 重新展示该 Say 对话。</para>
    /// </summary>
    public bool Rollforward()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        if (checkpoints == null || currentPos < 0 || currentPos >= checkpoints.Count - 1) return false;

        currentPos++;
        var cp = checkpoints[currentPos];

        // 恢复状态快照
        RestoreCheckpointState(cp);

        // 设置 DSL 状态
        _state.Set(StateKeys.Dsl.CurrentIndex, cp.CommandIndex);
        _state.Set(StateKeys.Dsl.WaitingType, "dialog");
        _state.Set(StateKeys.Dsl.Executing, true);

        // 更新回溯位置
        _state.Set(StateKeys.Rollback.CurrentIndex, currentPos);
        _state.Set(StateKeys.Rollback.IsActive, currentPos < checkpoints.Count - 1);

        // 重新展示该 Say
        var commands = _state.Get<List<ICommand>>(StateKeys.Dsl.Commands);
        if (commands != null && cp.CommandIndex < commands.Count)
        {
            _pipeline.SendAsync(commands[cp.CommandIndex]);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[DslExecutor] Rollforward -> pos={currentPos}, cmdIdx={cp.CommandIndex}");
        return true;
    }

    // ========== 检查点内部实现 ==========

    /// <summary>
    /// 在执行 ShowDialogCommand 前创建检查点
    /// <para>快照当前全量状态（排除回溯自身键）。</para>
    /// <para>如果用户回退后又走了新分支，截断未来的检查点。</para>
    /// </summary>
    private void CreateCheckpoint(int commandIndex)
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints) ?? new List<RollbackCheckpoint>();
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);

        // 如果在回退状态下有新 Say，截断未来检查点（新时间线）
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

        checkpoints.Add(new RollbackCheckpoint
        {
            CommandIndex = commandIndex,
            StateSnapshot = snapshot
        });

        // 超出上限时丢弃最旧的
        while (checkpoints.Count > MaxCheckpoints)
            checkpoints.RemoveAt(0);

        _state.Set(StateKeys.Rollback.Checkpoints, checkpoints);
        _state.Set(StateKeys.Rollback.CurrentIndex, checkpoints.Count - 1);
        _state.Set(StateKeys.Rollback.IsActive, false);  // 在前沿

        System.Diagnostics.Debug.WriteLine(
            $"[DslExecutor] Checkpoint created: pos={checkpoints.Count - 1}, cmdIdx={commandIndex}");
    }

    /// <summary>
    /// 恢复检查点状态：先清除当前状态，再写入快照
    /// </summary>
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

    /// <summary>清除所有回溯检查点（场景切换/新故事加载时调）</summary>
    public void ClearCheckpoints()
    {
        _state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint>());
        _state.Set(StateKeys.Rollback.CurrentIndex, -1);
        _state.Set(StateKeys.Rollback.IsActive, false);
    }
}
