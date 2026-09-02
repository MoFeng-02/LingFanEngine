using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Logging;

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
    private readonly IEngineLogger _logger;
    private IStoryRegistry? _storyRegistry;

    /// <summary>检查点列表锁——保护 List&lt;RollbackCheckpoint&gt; 的并发访问
    /// （DSL 线程 CreateCheckpoint vs Pipeline 线程 Rollback/Rollforward）</summary>
    private readonly object _checkpointLock = new();

    /// <summary>脏键追踪接口（若 StateContainer 实现了 IDirtyTracking 则非 null）
    /// 用于检查点增量深拷贝：仅拷贝变更过的键，未变更键复用上一检查点的深拷贝</summary>
    private readonly IDirtyTracking? _dirtyTracking;

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
        // 「nvl auto」作用域标记同样是播放模式状态，回溯不应恢复（与 AutoActive 同列）
        StateKeys.Nvl.AutoScoped,
        // 通知瞬时态：回溯到 Toast 显示中的检查点会令 Notify.Active 残留 true
        // （renderer 无对应 Toast）→ 后续通知全部排队永不显示
        StateKeys.Notify.Text,
        StateKeys.Notify.Type,
        StateKeys.Notify.Duration,
        StateKeys.Notify.Active,
        StateKeys.Notify.Queue,
    };

    /// <summary>C# 场景回溯回调（由 GameLoop 设置，回溯到 C# 场景时调用）</summary>
    public Func<string, Task>? OnCSharpSceneReplay { get; set; }

    public DslExecutor(IStateContainer state, ICommandPipeline pipeline, LingFanEngineOptions? options = null,
        IAsyncWaitService? waitService = null,
        IEventScheduler? eventScheduler = null,
        IEngineLoggerFactory? loggerFactory = null)
    {
        _state = state;
        _dirtyTracking = state as IDirtyTracking;
        _pipeline = pipeline;
        _options = options ?? new LingFanEngineOptions();
        // waitService 可为 null（仅测试场景——测试不执行 RunAsync 中的交互等待方法）
        _waitService = waitService!;
        _eventScheduler = eventScheduler;
        _logger = loggerFactory?.Create("DslExecutor") ?? NullEngineLogger.Instance;
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

    /// <summary>
    /// 在每条命令执行前把当前作用域写入状态：file 来自命令携带的 SourceFile，label 由 labels 映射反查
    /// 「最近前置 label」得到。当前场景由 SceneCommand / NavigateCommand 维护（__current_scene_name）。
    /// 供 let/local 的局部作用域键使用（file + scene + label 三维隔离，同级/兄弟作用域互不冲突）。
    /// </summary>
    private void ApplyCommandScope(ICommand cmd, int currentIndex)
    {
        if (cmd is IFileScopedCommand fs && !string.IsNullOrEmpty(fs.SourceFile))
        {
            // 文件边界切换：离开旧文件时清掉其文件级局部（类 JS 块级作用域——
            // 出文件作用域即不存在，下次进入由顶层 let 自然重建）。场景/标签级局部随场景切换已清，
            // 此处只清文件级（_local_<file>_ 前缀，不含 S_/L_ 场景/标签段）。
            var prevFile = _state.Get<string>(StateKeys.Scene.CurrentFile);
            if (!string.IsNullOrEmpty(prevFile)
                && !string.Equals(prevFile, fs.SourceFile, StringComparison.Ordinal))
            {
                LocalScope.ClearFileLevel(_state, prevFile);
            }
            _state.Set(StateKeys.Scene.CurrentFile, fs.SourceFile);
        }

        var sceneName = _state.Get<string>(StateKeys.Scene.CurrentName);
        var label = NearestPrecedingLabel(currentIndex);
        // 场景在流程中以 label <sceneName> 表示——最近前置 label 等于当前场景名时，
        // 视为「场景级作用域」而非独立的子标签作用域，避免键重复（_local_<file>_<scene>_<scene>_x）。
        // 仅当最近前置 label 与场景名不同时，才视为场景内的子标签作用域。
        if (label != null && !string.IsNullOrEmpty(sceneName)
            && string.Equals(label, sceneName, StringComparison.Ordinal))
        {
            label = null;
        }

        if (label != null)
            _state.Set(StateKeys.Scene.CurrentLabel, label);
        else if (_state.ContainsKey(StateKeys.Scene.CurrentLabel))
            _state.Remove(StateKeys.Scene.CurrentLabel);
    }

    private string? NearestPrecedingLabel(int currentIndex)
    {
        var labels = _state.Get<Dictionary<string, int>>(StateKeys.Dsl.Labels)
                    ?? _state.Get<IReadOnlyDictionary<string, int>>(StateKeys.Dsl.Labels) as Dictionary<string, int>;
        if (labels == null || labels.Count == 0) return null;
        string? best = null;
        int bestIdx = -1;
        foreach (var (name, idx) in labels)
        {
            if (idx <= currentIndex && idx > bestIdx)
            {
                bestIdx = idx;
                best = name;
            }
        }
        return best;
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
                    _logger.LogDebug($"自动加载 label [{label}] 来自 {filePath}");
                    return;
                }
            }
        }

        _logger.LogWarning($"Label [{label}] 未找到");
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
                            // NVL 模式：scene_idle 检查点不改 Nvl.Text（与末句累积文本完全相同），
                            // 若照常创建会制造"尾部视觉重复检查点"，破坏 Rollback 的 frontier 索引算术——
                            // 首下回退会落到"全展示"而非上一句，且后续回退全部指向同一全展示状态。
                            // 跳过它：末句检查点已代表"全展示"场景。仍推进 frontier 使其指向末句，
                            // 同时修复 CurrentIndex 与 checkpoints.Count 因混合非交互命令产生的偏差。
                            if (IsNvlSceneIdleRedundant(cps))
                            {
                                AdvanceRollbackFrontier();
                            }
                            else
                            {
                                _state.Set(StateKeys.Dialog.Text, "");
                                _state.Set(StateKeys.Dialog.Speaker, "");
                                _state.Set(StateKeys.Dialog.Complete, false);
                                CreateCheckpoint(currentIndex, "scene_idle");
                                AdvanceRollbackFrontier();
                            }
                        }
                    }
                    _state.Set(StateKeys.Rollback.IsActive, false);
                    _state.Set(StateKeys.Rollback.IsReplay, false);
                    _state.Set(StateKeys.Dsl.Executing, false);
                    break;
                }

                var cmd = commands[currentIndex];

                // 作用域注入：在每条命令执行前把 file/label 写入状态，供 let/local 的局部作用域键使用
                ApplyCommandScope(cmd, currentIndex);

                switch (cmd)
                {
                    // ========== 交互命令（async/await 等待）==========

                    case ShowDialogCommand dialog:
                    {
                        if (_state.Get<bool>(StateKeys.Rollback.IsReplay))
                        {
                            // 回溯重放：状态已从检查点恢复，跳过命令执行（不发 pipeline）。
                            // 只需等待用户点击前进——Dialog.Text 已从检查点恢复正确。
                            // 必须设置 WaitingType=Dialog：否则 UpdateDialogMask 判定遮罩不可见
                            // （遮罩仅在 WaitingType∈{Dialog,WaitSkipable,Pause} 且 !Clickable 时显示），
                            // 导致回溯浏览 say 时对话模态遮罩缺失（用户报告的"Say 遮罩消失"）。
                            _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Dialog);
                            _state.Set(StateKeys.Dialog.Complete, false);
                            await WaitForDialogComplete(ct);
                            if (ct.IsCancellationRequested) return;
                            _state.Set(StateKeys.Dsl.WaitingType, "");

                            if (CanRollforward())
                            {
                                if (Rollforward())
                                    return;
                            }
                            // 没有更多检查点了，退出回放模式，继续正常执行
                        }
                        else
                        {
                            // 正常执行：发送命令 → handler 累积 → 等待点击
                            await _pipeline.SendAsync(cmd, ct);
                            _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Dialog);

                            // 清除陈旧的 Dialog.Complete——防止上一句的用户点击（双击/快速点击/键盘）
                            // 残留的 true 被 WaitForDialogComplete 消费，导致跳过本句等待。
                            // DialogBox._clickConsumed 处理最常见的对话框点击场景，
                            // 此处作为 defense-in-depth 覆盖 SceneView 点击和键盘快捷键。
                            _state.Set(StateKeys.Dialog.Complete, false);

                            await WaitForDialogComplete(ct);
                            if (ct.IsCancellationRequested) return;

                            _state.Set(StateKeys.Dsl.WaitingType, "");
                            // 重置 Clickable——防止 say clickable=true 的状态泄漏到后续非 say 命令
                            _state.Set(StateKeys.Dialog.Clickable, false);
                            // Phase 37: 重置 Noskip——防止 say noskip=true 的状态泄漏
                            _state.Set(StateKeys.Dialog.Noskip, false);

                            // 检查点创建移到用户点击后（捕获用户所见状态）
                            // 而非命令执行前——NVL 累积模式下执行前状态缺少本次文本。
                            CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.Dialog);
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
                        // 清除对话框状态——防止上一句 say 的文本残留在对话框中
                        _state.Set(StateKeys.Dialog.Text, "");
                        _state.Set(StateKeys.Dialog.Speaker, "");
                        _state.Set(StateKeys.Dialog.Clickable, false);
                        _state.Set(StateKeys.Dialog.Complete, false);

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

                        // menu 是分支决策命令：回溯重放中玩家重新选择必须开辟新时间线。
                        // 绝不能走 Rollforward()——那会沿"第一次选择"的旧时间线前进、
                        // 完全忽略玩家的新 selectedIdx（用户报告的"回溯后仍是第一次选的项"根因）。
                        var wasReplayMenu = _state.Get<bool>(StateKeys.Rollback.IsReplay);
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        if (wasReplayMenu)
                            TruncateForwardCheckpoints(); // 丢弃旧分支的前向检查点
                        else
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
                        // 清除对话框状态——防止上一句 say 的文本残留在对话框中
                        _state.Set(StateKeys.Dialog.Text, "");
                        _state.Set(StateKeys.Dialog.Speaker, "");
                        _state.Set(StateKeys.Dialog.Clickable, false);
                        _state.Set(StateKeys.Dialog.Complete, false);

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

                        // input 与 menu 同理是决策命令：回溯重放中玩家的新输入必须开辟新时间线，
                        // 不能 Rollforward() 沿旧时间线前进而丢弃新输入。
                        var wasReplayInput = _state.Get<bool>(StateKeys.Rollback.IsReplay);
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        if (wasReplayInput)
                            TruncateForwardCheckpoints();
                        else
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
                            var conditionMet = TryEvaluateCondition(br.Condition, currentIndex);
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
                    {
                        var key = LocalScope.ResolveKey(_state, sv.Key);
                        if (sv.IsDefine && _state.ContainsKey(key))
                        {
                            // define ... once：跳过
                        }
                        else if (sv.Value is DslForLengthPlaceholder forLen)
                        {
                            var (ok, source) = TryEvaluateValue(forLen.SourceExpr, currentIndex);
                            var len = ok ? source switch
                            {
                                string s => s.Length,
                                System.Collections.IList list => list.Count,
                                System.Collections.IEnumerable en => en.Cast<object?>().Count(),
                                _ => 0
                            } : 0;
                            _state.Set(key, len);
                        }
                        else if (sv.Value is DslForIndexPlaceholder forIdx)
                        {
                            var (ok, source) = TryEvaluateValue(forIdx.SourceExpr, currentIndex);
                            var index = _state.Get<int>(forIdx.IndexVar);
                            object? element = null;
                            if (ok && source is System.Collections.IList list && index >= 0 && index < list.Count)
                                element = list[index]!;
                            _state.Set(key, element);
                        }
                        else if (sv.Value is DslExpressionPlaceholder placeholder)
                        {
                            var (ok, result) = TryEvaluateValue(placeholder.Expression, currentIndex);
                            if (ok)
                                _state.Set(key, result);
                        }
                        else
                        {
                            _state.Set(key, sv.Value);
                        }
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    case TransitionCommand:
                        await _pipeline.SendAsync(cmd, ct);
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
                            // 清除对话框状态——防止上一句 say 的文本残留在对话框中
                            _state.Set(StateKeys.Dialog.Text, "");
                            _state.Set(StateKeys.Dialog.Speaker, "");
                            _state.Set(StateKeys.Dialog.Clickable, false);
                            _state.Set(StateKeys.Dialog.Complete, false);

                            // Phase 24: 设置传入参数
                            if (cs.Params != null)
                                _state.Set(StateKeys.Screen.Params, cs.Params);
                            else
                                _state.Set<object?>(StateKeys.Screen.Params, null);

                            if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                                CreateCheckpoint(currentIndex, StateKeys.Dsl.WaitingTypes.CallScreen);

                            _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.CallScreen);
                            _state.Set<object?>(StateKeys.Screen.Result, null);
                            await _pipeline.SendAsync(new NavigateCommand { Path = cs.SceneName }, ct);
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
                        await _pipeline.SendAsync(slCmd, ct);
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
                        await _pipeline.SendAsync(cmd, ct);
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;

                    case NvlCommand nvlCmd:
                    {
                        // nvl 非交互命令（enter/clear/exit）。
                        // 仅 nvl clear 建立检查点："清零 NVL" 是一个有意义的叙事节点——
                        // 否则从 nvl clear 后的 say 回退会直接跳回清理前的多行块，跳过"已清空"状态
                        // （用户报告的 NVL 回退"多点几次/回到错的位置"根因）。
                        // 重放(IsReplay)时跳过 SendAsync：快照已含正确的 Nvl.Text，
                        // 重跑 handler 会错误地清/改文本（RestoreCheckpointState 已恢复目标状态）。
                        if (!_state.Get<bool>(StateKeys.Rollback.IsReplay))
                        {
                            await _pipeline.SendAsync(cmd, ct);
                            if (nvlCmd.IsClear)
                                CreateCheckpoint(currentIndex, "nvl_clear");
                        }
                        _state.Set(StateKeys.Rollback.IsActive, false);
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        _state.Set(StateKeys.Dsl.CurrentIndex, currentIndex + 1);
                        break;
                    }

                    default:
                        await _pipeline.SendAsync(cmd, ct);
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
            // 未知异常（引擎自身缺陷）——终止执行流，但必须复位交互等待状态：
            // 否则 WaitingType/Dialog.Complete 残留会让对话遮罩永久显示（用户视角的"游戏卡死"）
            _logger.LogError($"RunAsync error @ index {_state.Get<int>(StateKeys.Dsl.CurrentIndex)}: {ex.Message}", ex);
            _state.Set(StateKeys.Dsl.WaitingType, "");
            _state.Set(StateKeys.Dialog.Complete, false);
            _state.Set(StateKeys.Dsl.Executing, false);
        }
    }

    // ========== 语句级表达式安全求值（错误隔离） ==========

    /// <summary>
    /// 条件表达式安全求值（if/elif 分支条件）。
    /// <para>求值异常（如 "a" > 1 字符串与数字比较、未定义变量参与算术）不终止整个故事执行流：
    /// 记录含命令索引的日志并按 false 处理（走 else 分支），后续语句继续执行。
    /// 修复：原先异常冒泡到 RunAsync 外层 catch 会静默终止全部执行流。</para>
    /// </summary>
    private bool TryEvaluateCondition(string? expr, int currentIndex)
    {
        if (string.IsNullOrWhiteSpace(expr)) return false;
        try
        {
            return DslExpressionEvaluator.EvaluateBool(expr, _state);
        }
        catch (Exception ex)
        {
            _logger.LogError($"条件表达式求值失败 @ index {currentIndex} [\"{expr}\"]，按 false 处理并继续: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 值表达式安全求值（set 赋值 / for 长度与元素）。
    /// <para>求值异常不终止执行流：记录日志并标记失败（调用方跳过本次赋值，变量保持原值）。
    /// 返回 (false, null) 表示求值失败；求值结果本身为 null 时返回 (true, null)。</para>
    /// </summary>
    private (bool Ok, object? Value) TryEvaluateValue(string expr, int currentIndex)
    {
        try
        {
            return (true, DslExpressionEvaluator.Evaluate(expr, _state));
        }
        catch (Exception ex)
        {
            _logger.LogError($"表达式求值失败 @ index {currentIndex} [\"{expr}\"]，跳过本次赋值并继续: {ex.Message}", ex);
            return (false, null);
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

            // Phase 63 修复：防止已注销/已触发的单次事件执行
            // 事件在入队后可能被 Temporary/Permanent 模式注销，或单次事件已触发。
            // 通过 IsBlocked 检查跳过已销毁/已挂起/已触发的单次事件。
            // Normal 模式注销不加标记，IsBlocked 返回 false，已入队的事件仍会执行（软注销语义）。
            if (_eventScheduler.IsBlocked(evt.Id))
                continue;

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
_logger.LogWarning($"时间事件条件求值失败 [{evt.Id}]: {ex.Message}");
                    continue;
                }
            }

_logger.LogInfo($"执行时间事件 [{evt.Id}] - {evt.Description ?? "(无描述)"}");

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
_logger.LogError($"时间事件执行异常 [{evt.Id}]", ex);
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
        // 作用域注入：时间事件回调命令携带 SourceFile，注入 __current_file；
        // 时间事件不在任何 label 内，清除 __current_label 以免误用主循环残留的标签作用域
        //（否则回调中的 let/local 会被错误归入之前某条 label 的作用域键）。
        if (cmd is IFileScopedCommand teFs && !string.IsNullOrEmpty(teFs.SourceFile))
        {
            _state.Set(StateKeys.Scene.CurrentFile, teFs.SourceFile);
            if (_state.ContainsKey(StateKeys.Scene.CurrentLabel))
                _state.Remove(StateKeys.Scene.CurrentLabel);
        }

        switch (cmd)
        {
            case ShowDialogCommand:
                await _pipeline.SendAsync(cmd, ct);
                _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Dialog);
                _state.Set(StateKeys.Dialog.Complete, false);
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
                await _pipeline.SendAsync(cmd, ct);
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
                await _pipeline.SendAsync(new NavigateCommand { Path = cs.SceneName }, ct);
                await WaitForScreenResult(ct);
                _state.Set(StateKeys.Dsl.WaitingType, "");
                break;

            default:
                // 非交互命令——直接发送到管道
                await _pipeline.SendAsync(cmd, ct);
                break;
        }
    }

    // ========== 异步等待方法 ==========

    /// <summary>交互等待超时上限（秒）——防止状态键 bug 导致永久挂起</summary>
    private const double InteractionTimeoutSeconds = 300;

    private async Task WaitForDialogComplete(CancellationToken ct)
    {
        // Fast path 已移除——防止陈旧的 Dialog.Complete=true（来自上一句交互的残留点击）
        // 被直接消费，导致跳过当前句的等待。
        // 调用方在调用前已清除 Dialog.Complete=false，WaitForAsync 的 fast path 会正确处理。

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<bool>(StateKeys.Dialog.Complete),
                TimeSpan.FromSeconds(InteractionTimeoutSeconds),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("WaitForDialogComplete 超时(300s)，强制推进");
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
            _logger.LogWarning("WaitForTransitionComplete: 等待激活超时(5s)，跳过等待");
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
            _logger.LogWarning("WaitForTransitionComplete 超时(60s)，强制推进");
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
            _logger.LogWarning("WaitForMenuSelection 超时(300s)，返回 -1");
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
            _logger.LogWarning("WaitForInput 超时(300s)，返回空字符串");
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
            _logger.LogWarning("WaitForScreenResult 超时(300s)，强制推进");
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

    /// <summary>
    /// 计算回退目标检查点索引。
    /// <para>无有效目标（已在最前 / 目标为场景重放 csharp_scene 检查点）时返回 -1。</para>
    /// </summary>
    private int ComputeRollbackTarget()
    {
        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        if (checkpoints == null || checkpoints.Count == 0) return -1;
        var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        if (currentPos <= 0) return -1;

        var targetPos = currentPos - 1;
        // 当前位于 frontier 末端：末位检查点存储的是"当前可见状态"，需再跳过它一步。
        if (currentPos >= checkpoints.Count)
            targetPos--;

        if (targetPos < 0) return -1;

        // C# 场景检查点（csharp_scene）允许回退——回退到此 = 场景级回溯（重跑整个 StoryScript.RunAsync）。
        // C# 场景内部的 SayAsync/ShowMenuAsync 不创建逐句检查点，故其回溯精度天然为场景级，符合设计。
        // 【历史】曾在此拒绝回退到 csharp_scene 以防"NVL 逐句回退击穿到场景级"，但那是误判：
        // NVL 是 DSL 脚本场景，根本不产生 csharp_scene 检查点（仅 C# StoryScript 场景入口才创建，
        // 见 NavigateHandler.HandleScriptEntry）；NVL 尾部冗余的真正修复是 IsNvlSceneIdleRedundant
        // 创建期抑制。该护栏纯属过度防御，反而掐死了 C# 场景的合法回溯，现移除。
        return targetPos;
    }

    /// <inheritdoc/>
    public bool CanRollback()
    {
        lock (_checkpointLock)
        {
            return ComputeRollbackTarget() >= 0;
        }
    }

    /// <inheritdoc/>
    public bool CanRollforward()
    {
        lock (_checkpointLock)
        {
            var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
            var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
            return checkpoints != null && currentPos >= 0 && currentPos < checkpoints.Count;
        }
    }

    /// <inheritdoc/>
    public bool RollbackTo(int targetPos)
    {
        lock (_checkpointLock)
        {
            var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
            if (checkpoints == null || targetPos < 0 || targetPos >= checkpoints.Count) return false;

            var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
            if (targetPos >= currentPos) return false;

            RestoreAndRestart(checkpoints[targetPos], targetPos, checkpoints.Count);
            return true;
        }
    }

    /// <inheritdoc/>
    public bool Rollback()
    {
        lock (_checkpointLock)
        {
            var targetPos = ComputeRollbackTarget();
            if (targetPos < 0) return false;

            var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
            if (checkpoints == null) return false;

            RestoreAndRestart(checkpoints[targetPos], targetPos, checkpoints.Count);
            return true;
        }
    }

    /// <inheritdoc/>
    public bool Rollforward()
    {
        lock (_checkpointLock)
        {
            var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
            var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
            if (checkpoints == null || currentPos < 0 || currentPos >= checkpoints.Count) return false;

            var targetPos = currentPos + 1;

            if (targetPos >= checkpoints.Count)
            {
                // 回到前沿（live）：恢复末位检查点（用户离开 live 时的可见状态），
                // 跳过其交互命令、从下一条命令继续正常执行（exitReplay）。
                // 修复（2026-09 滚轮历史"前进进不去"BUG）：旧实现 IsReplay 恒 true——
                // 末句以重放模式等待点击且 CanRollforward 恒 false，滚轮下无响应，
                // 用户必须额外点击一次才能回 live（感知为"2、3 进不去/卡住"）。
                var last = checkpoints[^1];
                if (last.CommandIndex >= 0)
                {
                    RestoreAndRestart(last, checkpoints.Count, checkpoints.Count, exitReplay: true);
                    return true;
                }
                // csharp_scene 检查点（CommandIndex<0）：保持场景级重放语义
                RestoreAndRestart(last, checkpoints.Count, checkpoints.Count);
                return true;
            }

            RestoreAndRestart(checkpoints[targetPos], targetPos, checkpoints.Count);
            return true;
        }
    }

    // ========== 检查点内部实现 ==========

    private void AdvanceRollbackFrontier()
    {
        lock (_checkpointLock)
        {
            var cps = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
            if (cps != null && cps.Count > 0)
                _state.Set(StateKeys.Rollback.CurrentIndex, cps.Count);
        }
    }

    /// <summary>
    /// 决策命令（menu/input）回溯重放后玩家做出新选择时调用：
    /// 丢弃当前决策检查点之后的旧时间线检查点（新决策使旧分支失效），并将前沿设到末端。
    /// <para>currentPos 指向刚重放的决策命令检查点本身（回溯时由 RestoreAndRestart 设置），
    /// 保留 [0..currentPos]（含决策检查点），截断其后旧分支——后续命令的 CreateCheckpoint 会 append 新分支。</para>
    /// </summary>
    private void TruncateForwardCheckpoints()
    {
        lock (_checkpointLock)
        {
            var cps = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
            if (cps == null || cps.Count == 0) return;
            var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
            if (currentPos >= 0 && currentPos + 1 < cps.Count)
            {
                cps.RemoveRange(currentPos + 1, cps.Count - currentPos - 1);
                _state.Set(StateKeys.Rollback.Checkpoints, cps);
            }
            // 前沿指向截断后的末端（frontier），后续 CreateCheckpoint 直接 append
            _state.Set(StateKeys.Rollback.CurrentIndex, cps.Count);
        }
    }

    private void RestoreAndRestart(RollbackCheckpoint cp, int targetPos, int totalCheckpoints, bool exitReplay = false)
    {
        // 递增 C# 场景回放代次——使过期的 C# 场景 Runner 中的 SayAsync 等阻塞调用提前返回
        var gen = _state.Get<int>(StateKeys.Dsl.CSharpReplayGeneration) + 1;
        _state.Set(StateKeys.Dsl.CSharpReplayGeneration, gen);

        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
        cts?.Dispose();
        Interlocked.Exchange(ref _runTask, null);

        // 清空管道中的陈旧命令——防止回溯前已 SendAsync 但 GameLoop 尚未处理的命令
        // 在 RestoreCheckpointState 之后被 GameLoop 处理，污染已恢复的状态。
        // 必须在 RestoreCheckpointState 之前调用：此时 GameLoop 即使读到陈旧命令，
        // 也只是修改回溯前的状态（即将被 RestoreCheckpointState 覆盖），不会影响正确性。
        _pipeline.Clear();

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

        // exitReplay（Rollforward 回到前沿）：跳过该检查点的交互命令（已完整展示），从下一条继续正常执行
        _state.Set(StateKeys.Dsl.CurrentIndex, exitReplay ? cp.CommandIndex + 1 : cp.CommandIndex);
        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Dsl.Executing, true);
        _state.Set(StateKeys.Scene.Dirty, true);

        _state.Set(StateKeys.Rollback.CurrentIndex, targetPos);
        // IsActive 语义 = 回溯浏览中（可前进）——与 CanRollforward 的 currentPos < count 对齐。
        // 旧公式 targetPos < total-1 在最后一个检查点误报 false（明明可前进）。
        _state.Set(StateKeys.Rollback.IsActive, targetPos < totalCheckpoints);
        // IsReplay = 重放模式（恢复后不重发命令、等待点击前进）；exitReplay 例外——直接回 live
        _state.Set(StateKeys.Rollback.IsReplay, !exitReplay);

        // 清除脏键：RestoreCheckpointState 的 Set 已标记所有恢复键为脏，
        // 但恢复后的值与目标检查点的快照一致（RestoreCheckpointState 从该检查点深拷贝而来）。
        // 下一个 CreateCheckpoint 可安全复用目标检查点的深拷贝，无需再次全量深拷贝。
        // 新 RunAsync 中命令产生的 Set 会重新标记脏键，确保增量追踪正确。
        if (_dirtyTracking != null)
            _dirtyTracking.GetSnapshotAndClearDirty();

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

        lock (_checkpointLock)
        {
            var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints) ?? new List<RollbackCheckpoint>();
            var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);

            if (currentPos >= 0 && currentPos + 1 < checkpoints.Count)
                checkpoints.RemoveRange(currentPos + 1, checkpoints.Count - currentPos - 1);

            var snapshot = CreateIncrementalSnapshot(checkpoints);

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
    }

    /// <summary>
    /// NVL 模式下，scene_idle 检查点是否为"冗余视觉重复"——其 Nvl.Text 与上一检查点完全相同。
    /// <para>仅当 NVL 激活、上一检查点存在、且当前累积 Nvl.Text 等于上一检查点的 Nvl.Text 时成立
    /// （scene_idle 块只清空 Dialog.Text/Speaker，从不改动 Nvl.Text，故末句与 scene_idle 视觉一致）。</para>
    /// </summary>
    private bool IsNvlSceneIdleRedundant(List<RollbackCheckpoint>? cps)
    {
        if (!_state.Get<bool>(StateKeys.Nvl.Active)) return false;
        if (cps == null || cps.Count == 0) return false;
        var currentNvl = _state.Get<string>(StateKeys.Nvl.Text) ?? "";
        if (!cps[^1].StateSnapshot.TryGetValue(StateKeys.Nvl.Text, out var prevNvl) || prevNvl is not string prevStr)
            return false;
        return string.Equals(currentNvl, prevStr, StringComparison.Ordinal);
    }

    private void CreateCheckpoint(int commandIndex, string interactionType = StateKeys.Dsl.WaitingTypes.Dialog)
    {
        // 线程安全：如果 CTS 已取消（回溯/停止中），不要创建过期检查点
        var cts = _cts;
        if (cts == null || cts.IsCancellationRequested) return;

        // Phase 60: 小说世界模式禁用逐句回溯——时间锚点存档是唯一的"历史"
        if (_options.EnableTimeSystem) return;

        // Phase 24: block_rollback——如果当前命令索引 >= 阻止标记，跳过检查点创建
        var blockedUntil = _state.Get<int>(StateKeys.Rollback.BlockedUntil);
        if (blockedUntil >= 0 && commandIndex >= blockedUntil)
            return;

        var currentType = _state.Get<int>(StateKeys.Scene.CurrentType);
        if ((SceneType)currentType != SceneType.Game)
            return;

        lock (_checkpointLock)
        {
            // 双重检查：获锁后再次确认未取消（可能在等待锁期间被回溯取消）
            cts = _cts;
            if (cts == null || cts.IsCancellationRequested) return;

            var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints) ?? new List<RollbackCheckpoint>();
            var currentPos = _state.Get<int>(StateKeys.Rollback.CurrentIndex);

            if (currentPos >= 0 && currentPos + 1 < checkpoints.Count)
            {
                checkpoints.RemoveRange(currentPos + 1, checkpoints.Count - currentPos - 1);
            }

            var snapshot = CreateIncrementalSnapshot(checkpoints);

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
    }

    /// <summary>
    /// 创建增量状态快照——仅深拷贝脏键，未变更键复用上一检查点的深拷贝。
    /// <para>若 IDirtyTracking 不可用（StateContainer 未实现该接口），回退到全量深拷贝。</para>
    /// <para>安全性：检查点的 StateSnapshot 创建后不可变；RestoreCheckpointState 恢复时会再次深拷贝，
    /// 因此多个检查点共享同一深拷贝引用是安全的。</para>
    /// </summary>
    private Dictionary<string, object?> CreateIncrementalSnapshot(List<RollbackCheckpoint> checkpoints)
    {
        var snapshot = new Dictionary<string, object?>();

        // 获取上一检查点的快照（用于复用未变更键的深拷贝）
        Dictionary<string, object?>? prevSnapshot = checkpoints.Count > 0
            ? checkpoints[^1].StateSnapshot
            : null;

        if (_dirtyTracking != null && prevSnapshot != null)
        {
            // 增量模式：原子获取快照+脏键（写锁保证一致性）
            var (currentSnapshot, dirtyKeys) = _dirtyTracking.GetSnapshotAndClearDirty();
            var dirtySet = dirtyKeys as HashSet<string> ?? new HashSet<string>(dirtyKeys, StringComparer.Ordinal);

            foreach (var (k, v) in currentSnapshot)
            {
                if (s_rollbackKeys.Contains(k))
                    continue;

                if (dirtySet.Contains(k) || !prevSnapshot.ContainsKey(k))
                {
                    // 脏键或新键 → 深拷贝当前值
                    snapshot[k] = DeepCopyMutable(k, v);
                }
                else
                {
                    // 非脏键且上一检查点有此键 → 复用上一检查点的深拷贝
                    snapshot[k] = prevSnapshot[k];
                }
            }
        }
        else
        {
            // 回退模式：全量深拷贝（IDirtyTracking 不可用或首检查点无前驱）
            IReadOnlyDictionary<string, object?> currentSnapshot;
            if (_dirtyTracking != null)
            {
                // 首检查点：仍通过 IDirtyTracking 获取快照并清除脏键（保持脏键状态一致）
                var (snap, _) = _dirtyTracking.GetSnapshotAndClearDirty();
                currentSnapshot = snap;
            }
            else
            {
                currentSnapshot = _state.GetSnapshot();
            }

            foreach (var (k, v) in currentSnapshot)
            {
                if (s_rollbackKeys.Contains(k))
                    continue;
                snapshot[k] = DeepCopyMutable(k, v);
            }
        }

        return snapshot;
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
        lock (_checkpointLock)
        {
            _state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint>());
            _state.Set(StateKeys.Rollback.CurrentIndex, -1);
            _state.Set(StateKeys.Rollback.IsActive, false);
            _state.Set(StateKeys.Rollback.IsReplay, false);
        }
    }
}
