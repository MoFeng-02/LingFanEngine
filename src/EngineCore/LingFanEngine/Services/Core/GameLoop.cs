using System.Diagnostics;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Media;
// Router 已移除
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Services.Media;
using LingFanEngine.Services.Saves;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 游戏主循环实现（后台线程 + 可配置帧率）
/// <para>帧循环在后台线程运行，通过 IUIThreadDispatcher.Post 触发 SceneView 更新。</para>
/// <para>帧率由 TargetFps 控制（0=不限，15~600=限制），使用 Stopwatch 自旋 + Task.Delay 混合节流。</para>
/// </summary>
public class GameLoop : IGameLoop
{
    private readonly LingFanEngineOptions _options;
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    private readonly IGameTimeService _time;
    private readonly ITweenEngine _tween;
    private readonly ISaveService? _saveService;
    private readonly ISceneRegistry? _sceneRegistry;
    private readonly ISceneStack? _sceneStack;
    private readonly IStoryRegistry? _storyRegistry;
    private readonly ITransitionEngine? _transitionEngine;
    private readonly IAudioManager? _audioManager;
    private readonly IVideoManager? _videoManager;
    private readonly IEventScheduler? _eventScheduler;
private readonly ITimeEventRegistry? _timeEventRegistry;
    private readonly IDslExecutor? _dslExecutor;
    private readonly IUIThreadDispatcher? _uiThreadDispatcher;
    private CancellationTokenSource? _stopCts;
    private Task? _loopTask;
    private int _targetFps = 60;
    private bool _firstFrame = true;

    /// <summary>
    /// UI 帧投递标志——防止 Dispatcher.Post 帧堆积导致抖动。
    /// true = 已投递但 UI 线程尚未处理，跳过本次投递。
    /// </summary>
    private volatile bool _uiFramePending;

    /// <summary>
    /// 跳帧期间累积的 delta——当 UI 帧被跳过时，frameDelta 累积到此字段，
    /// 下次投递时一并传递给 UI 线程，确保打字机/通知等基于 delta 的计时不受影响。
    /// </summary>
    private double _pendingDelta;
    /// <summary>
    /// 帧回调（由 GameLoop 每帧触发，SceneView.Update 通过此处注册）
    /// </summary>
    private Action<double>? _uiFrameAction;

    /// <summary>场景渲染器引用（截图用）</summary>
    private LingFanEngine.Abstractions.Interfaces.Views.ISceneRenderer? _sceneRenderer;

    /// <summary>场景名→SceneScriptEntry 映射（C# 剧情脚本，SceneType 决定存档/堆栈行为）</summary>
    private readonly Dictionary<string, Abstractions.Scripting.SceneScriptEntry> _scriptEntries = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>命令分发器——按类型路由到注册的处理器</summary>
    private readonly ICommandDispatcher _dispatcher;
    /// <summary>JSON 值转换器（从静态迁移为实例，解除 StateContainer→GameLoop 循环依赖）</summary>
    private readonly IJsonValueConverter _jsonConverter;
    /// <summary>命令处理上下文（提供给 handler 的依赖 facade）</summary>
    private readonly ICommandContext _context;

    // ── 拆分出的子服务 ──
    private readonly IStateInitializer _stateInitializer;
    private readonly IAnimationService _animationService;
    private readonly IShakeService _shakeService;
    private readonly IPlaybackService _playbackService;
    private readonly ISaveDataService _saveDataService;

    /// <summary>注册 SceneScriptEntry（由 CSharpScripts.RegisterAll 调用）</summary>
    public void RegisterScriptEntry(Abstractions.Scripting.SceneScriptEntry entry)
    {
        _scriptEntries[entry.SceneName] = entry;

        // Phase 63: 自动注册声明式时间事件到全局注册表 + EventScheduler
        // 设计理念：时间事件生命周期——事件一旦注册即独立，场景只是挂载器（出生地）
        if (entry.TimeEvents != null && entry.TimeEvents.Count > 0)
        {
            // 1. 纳入全局注册表（供 restore_time_event 和读档重注册跨场景查找）
            if (_timeEventRegistry != null)
            {
                foreach (var evt in entry.TimeEvents)
                {
                    _timeEventRegistry.RegisterDeclaration(evt);
                }
            }

            // 2. 注册到 EventScheduler（如果时间系统启用）
            if (_eventScheduler != null && _options.EnableTimeSystem)
            {
                foreach (var evt in entry.TimeEvents)
                {
                    _eventScheduler.RegisterEvent(evt);
                }
            }
        }
    }

    /// <inheritdoc/>
    public int TargetFps
    {
        get => _targetFps;
        set
        {
            if (value <= 0)
                _targetFps = 0; // 不限帧
            else
                _targetFps = Math.Clamp(value, 15, 600);
        }
    }

    /// <inheritdoc/>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <inheritdoc/>
    public event Action<double>? OnFrame
    {
        add
        {
            // 合并到 UI 线程回调中，避免外部直接订阅带来跨线程问题
            _uiFrameAction += value;
        }
        remove
        {
            _uiFrameAction -= value;
        }
    }

    public event Action<Exception, string>? OnException;

    /// <summary>
    /// 构造函数 — 全接口注入，无 new 依赖
    /// </summary>
    public GameLoop(
        ICommandPipeline pipeline,
        IStateContainer state,
        IGameTimeService time,
        ICommandDispatcher dispatcher,
        ITweenEngine tween,
        IJsonValueConverter jsonConverter,
        IEnumerable<IDefaultCommandHandler> defaultHandlers,
        IStateInitializer stateInitializer,
        IAnimationService animationService,
        IShakeService shakeService,
        IPlaybackService playbackService,
        ISaveDataService saveDataService,
        ISaveService? saveService = null,
        ISceneRegistry? sceneRegistry = null,
        LingFanEngineOptions? options = null,
        ISceneStack? sceneStack = null,
        IStoryRegistry? storyRegistry = null,
        IDslExecutor? dslExecutor = null,
        ITransitionEngine? transitionEngine = null,
        IAudioManager? audioManager = null,
        IVideoManager? videoManager = null,
        IEventScheduler? eventScheduler = null,
        ITimeEventRegistry? timeEventRegistry = null,
        IUIThreadDispatcher? uiThreadDispatcher = null)
    {
        _pipeline = pipeline;
        _state = state;
        _options = options ?? new LingFanEngine.Abstractions.EngineOptions.LingFanEngineOptions();
        _time = time;
        _dispatcher = dispatcher;
        _tween = tween;
        _jsonConverter = jsonConverter;
        _saveService = saveService;
        _sceneRegistry = sceneRegistry;
        _sceneStack = sceneStack;
        _storyRegistry = storyRegistry;
        _dslExecutor = dslExecutor;
        _transitionEngine = transitionEngine;
        _audioManager = audioManager;
        _videoManager = videoManager;
        _eventScheduler = eventScheduler;
        _timeEventRegistry = timeEventRegistry;
        _uiThreadDispatcher = uiThreadDispatcher;

        _stateInitializer = stateInitializer;
        _animationService = animationService;
        _shakeService = shakeService;
        _playbackService = playbackService;
        _saveDataService = saveDataService;

        // 连接 SaveDataService 回调
        if (_saveDataService is SaveDataService sds)
        {
            sds.OnResetInteractionState = ResetInteractionState;
            sds.TryGetScriptEntry = name => _scriptEntries.TryGetValue(name, out var entry) ? entry : null;
        }

        // 连接 DslExecutor 的 C# 场景回溯回调
        // 回溯到 C# 场景检查点时，重新执行 StoryScript.Run()
        if (_dslExecutor is DslExecutor dslExec)
        {
            dslExec.OnCSharpSceneReplay = async sceneName =>
            {
                if (_scriptEntries.TryGetValue(sceneName, out var entry))
                {
                    // 设置 C# 场景回放代次——SayAsync 等方法通过此值检测是否已被回溯/前进取消
                    var startGen = _state.Get<int>(StateKeys.Dsl.CSharpReplayGeneration);
                    GameController.CSharpReplayGen.Value = startGen;

                    try
                    {
                        // IsReplay 已由 RestoreAndRestart 设置为 true
                        // C# 场景的 SayAsync 通过管道发送 ShowDialogCommand，不经过 DslExecutor.RunAsync，
                        // 因此不会创建逐句检查点——符合 C# 场景仅场景级检查点的设计
                        await entry.Runner();
                    }
                    catch (CSharpSceneReplayCancelledException)
                    {
                        // 回溯/前进取消了此回放——Runner 已被异常终止，无需处理
                        System.Diagnostics.Debug.WriteLine($"[GameLoop] C# 场景回放 [{sceneName}] 被回溯/前进取消");
                    }
                    finally
                    {
                        // 清除 AsyncLocal 代次
                        GameController.CSharpReplayGen.Value = 0;
                    }

                    // 仅当代次未变化时重置——如果回溯/前进已启动新执行，不覆盖新设置的标志
                    var currentGen = _state.Get<int>(StateKeys.Dsl.CSharpReplayGeneration);
                    if (currentGen == startGen)
                    {
                        // Run() 完成后用户进入 idle 状态（与按钮交互）
                        // 重置 IsReplay，使后续 DSL 场景能正常创建检查点
                        _state.Set(StateKeys.Rollback.IsReplay, false);
                        _state.Set(StateKeys.Rollback.IsActive, false);
                    }
                }
            };
        }

        _context = new CommandContext(this);
        RegisterDefaultHandlers(defaultHandlers);
        _stateInitializer.Initialize(_state);

        // 根据 EngineOptions 初始化性能 HUD 可见性
        _state.Set(StateKeys.Performance.ShowHud, _options.ShowPerformanceHud);
    }


    /// <summary>
    /// 注册场景视图（UI 层引用，非 DI 管理——用于截图功能）
    /// </summary>
    public void SetSceneView(LingFanEngine.Abstractions.Interfaces.Views.ISceneRenderer view)
    {
        _sceneRenderer = view;
        // 连接截图回调到 SaveDataService
        if (_saveDataService is SaveDataService sds)
            sds.CaptureThumbnail = view == null ? null : () => view.CaptureThumbnail();
    }

    /// <summary>
    /// 补间引擎
    /// </summary>
    public ITweenEngine Tween => _tween;

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
            return Task.CompletedTask;

        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // 在后台线程运行主循环
        _loopTask = Task.Run(() => RunLoopAsync(_stopCts.Token), _stopCts.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        _stopCts?.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask; } catch (OperationCanceledException) { }
        }
        _pipeline.Complete();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        // 注意：targetFrameTicks 不在此处缓存——每帧从 _targetFps 动态计算，
        // 这样运行时修改 TargetFps 能立即生效。
        var stopwatch = Stopwatch.StartNew();
        var accumulatedTime = 0.0;
        var lastFrameTime = 0.0;
        var timeTickInterval = _options.SecondsPerGameMinute;

        // Windows 系统定时器默认分辨率 ~15.6ms，Task.Delay 的最小睡眠也是一个滴答。
        // 对 120 FPS（8.33ms/帧）使用 Task.Delay 会超睡到 15.6ms，把实际帧率拖回 ~60 FPS。
        // 解决方案：仅当剩余时间 > 16ms 时才用 Task.Delay，否则用 Thread.Sleep(0) + 自旋。
        var delayThresholdTicks = 16 * Stopwatch.Frequency / 1000; // 16ms

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frameStartTicks = stopwatch.ElapsedTicks;

                // 消费所有待处理命令
                while (_pipeline.TryRead(out var command))
                {
                    ProcessCommand(command);
                }

                // 补间插值——用实际帧间时间差
                var nowTicks = stopwatch.ElapsedTicks;
                var currentTime = (double)nowTicks / Stopwatch.Frequency;
                var frameDelta = Math.Min(currentTime - lastFrameTime, 0.1); // 上限 100ms 防跳帧
                lastFrameTime = currentTime;
                _tween.Update(frameDelta, _pipeline.TimeScale);

                // 过渡动画更新（TransitionEngine 自驱动计时，不依赖 TweenEngine）
                _transitionEngine?.Update(frameDelta);

                // 控件级动画推进（animate 命令每帧更新）
                _animationService.Update(frameDelta, _state);

                // 屏幕震动推进（每帧更新偏移量）
                _shakeService.Update(frameDelta, _state);

                // DSL 执行器现在异步运行（RunAsync），不再每帧调用 Step()
                // 仅消费管道中可能存在的命令（由 RunAsync 投递的）
                while (_pipeline.TryRead(out var newCmd))
                {
                    ProcessCommand(newCmd);
                }

                // 跳过/自动模式逻辑（DSL 执行器异步运行时，确保读到最新状态）
                _playbackService.Process(frameDelta, _state);

                // 累计时间，推进游戏时间（仅在启用时间系统时）
                if (_options.EnableTimeSystem && !_time.IsPaused)
                {
                    // _pipeline.TimeScale 影响全局（skip 模式加速一切）
                    // _time.TimeScale 仅影响游戏时间（可运行时调整，不影响命令执行速度）
                    accumulatedTime += _pipeline.TimeScale * _time.TimeScale * frameDelta;
                    while (accumulatedTime >= timeTickInterval)
                    {
                        _time.Tick();
                        accumulatedTime -= timeTickInterval;
                    }
                }
                else if (_time.IsPaused)
                {
                    // 暂停期间清零，恢复后从当前帧重新积累（不补暂停期间的时间）
                    accumulatedTime = 0;
                }

                // 性能指标收集（每帧更新到状态容器，SceneView 读取显示）
                UpdatePerformanceMetrics(frameDelta);

                // 视频结束事件轮询（检查 IsFinished 状态变化，触发 OnFinished 回调）
                if (_videoManager is VideoManager vm)
                    vm.PollFinished();

                // 触发帧回调（投递到 UI 线程执行 SceneView.Update）
                // 帧跳过逻辑：如果上一帧的 UI 更新尚未完成，跳过本次投递。
                // GameLoop 在后台线程以 120 FPS 更新状态，UI 线程按自身节奏读取最新状态。
                // 这避免了 Dispatcher.Post 队列堆积导致的动画抖动。
                if (_uiFrameAction != null && !_uiFramePending)
                {
                    // 合并跳帧期间累积的 delta，确保打字机/通知等计时准确
                    var delta = frameDelta + _pendingDelta;
                    _pendingDelta = 0;
                    var highPriority = _firstFrame;
                    _firstFrame = false;
                    _uiFramePending = true;
                    var dispatcher = _uiThreadDispatcher;
                    if (dispatcher != null)
                    {
                        dispatcher.Post(() =>
                        {
                            try
                            {
                                _uiFrameAction(delta);
                            }
                            finally
                            {
                                _uiFramePending = false;
                            }
                        }, highPriority);
                    }
                    else
                    {
                        // fallback：无调度器时同步执行（测试场景）
                        _uiFramePending = false;
                        try { _uiFrameAction(delta); } catch { /* ignore */ }
                    }
                }
                else if (_uiFrameAction != null)
                {
                    // 帧被跳过：累积 delta 供下次投递使用
                    _pendingDelta += frameDelta;
                }

                // 高精度帧率节流（_targetFps=0 时不限帧）
                if (_targetFps > 0)
                {
                    // 每帧动态计算 targetFrameTicks，支持运行时修改 TargetFps
                    var targetFrameTicks = Stopwatch.Frequency / (long)_targetFps;
                    var elapsedTicks = stopwatch.ElapsedTicks - frameStartTicks;
                    var remainingTicks = targetFrameTicks - elapsedTicks;
                    if (remainingTicks > 0)
                    {
                        var spinEnd = stopwatch.ElapsedTicks + remainingTicks;

                        // 仅当剩余时间 > 16ms（Windows 系统定时器分辨率）时才用 Task.Delay。
                        // 对高帧率（120/144 FPS，帧时间 6.9~8.3ms）使用 Task.Delay 会超睡到 15.6ms，
                        // 把实际帧率拖回 ~60 FPS。
                        if (remainingTicks > delayThresholdTicks)
                        {
                            var delayMs = (remainingTicks * 1000L) / Stopwatch.Frequency - 2;
                            if (delayMs > 1)
                            {
                                await Task.Delay((int)delayMs, ct);
                            }
                        }

                        // 剩余时间用 Thread.Sleep(0) + 自旋等待达到精确帧率。
                        // Thread.Sleep(0) 让出 CPU 时间片给同优先级线程，但不进入 15.6ms 系统定时器睡眠。
                        // Thread.SpinWait(1) 做亚毫秒级精确等待。
                        var spinCount = 0;
                        while (stopwatch.ElapsedTicks < spinEnd && !ct.IsCancellationRequested)
                        {
                            if (++spinCount % 16 == 0)
                                Thread.Sleep(0); // 定期让出 CPU，避免 100% 占用
                            else
                                Thread.SpinWait(1);
                        }
                    }
                } // if (_targetFps > 0)
            }
        }
        catch (OperationCanceledException ex)
        {
            // 正常停止
            OnException?.Invoke(ex, nameof(RunLoopAsync));
        }
        catch (Exception ex)
        {
            OnException?.Invoke(ex, nameof(RunLoopAsync));
        }
    }

    private void ProcessCommand(ICommand command)
    {
        _dispatcher.Dispatch(command, _context);
    }

    /// <summary>
    /// 从 DI 注入的处理器集合注册默认命令处理器（AOT 安全 — 显式类型判断，不使用反射）
    /// </summary>
    private void RegisterDefaultHandlers(IEnumerable<IDefaultCommandHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            switch (handler)
            {
                case ICommandHandler<SetVariableCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ShowDialogCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ExtendDialogCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<PlayBgmCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<PlaySeCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<PlayVoiceCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<BgmQueueCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<PlayAmbientCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<StopAmbientCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<TransitionCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<AnimateCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ShowHideCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<NavigateCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<SaveLoadCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<InputCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<WaitCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<HardPauseCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<BackCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ForwardCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<RollbackCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<RollforwardCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<RollbackToCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<SceneCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<NavToLabelCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<BuildSceneCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ClearStackCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ResetGameStateCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<MergeDefinesCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ShakeCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ToggleSkipCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ToggleAutoCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<UnlockGalleryCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<DebugLogCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<NvlCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<PlayVideoCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<StopVideoCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<PauseVideoCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ResumeVideoCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<SeekVideoCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<CutsceneCommand> h: _dispatcher.Register(h); break;
                // Phase 38: 时间事件与通知
                case ICommandHandler<TimeEventCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<SetTimeEventCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<UnregisterTimeEventCommand> h: _dispatcher.Register(h); break;
                // Phase 63: 恢复时间事件
                case ICommandHandler<RestoreTimeEventCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<TimePauseCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<TimeResumeCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<SkipTimeCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<NotifyCommand> h: _dispatcher.Register(h); break;
                // Phase 44-47: DSL 2.0 新命令处理器
                case ICommandHandler<ArrayPushCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ArrayPopCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<DictSetCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<SpriteCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<BgSwitchCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<Live2DCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<AchievementUnlockCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ChapterUnlockCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<SaveDeleteCommand> h: _dispatcher.Register(h); break;
                default:
                    Debug.WriteLine($"[GameLoop] 未知的默认处理器类型: {handler.GetType().Name}");
                    break;
            }
        }
    }

    /// <summary>
    /// 注册自定义命令处理器（供外部扩展使用）
    /// </summary>
    public void RegisterCommandHandler<TCommand>(ICommandHandler<TCommand> handler)
        where TCommand : ICommand
        => _dispatcher.Register(handler);


    /// <summary>
    /// 深合并场景变量定义到状态容器（委托到 SaveDataService 统一实现）
    /// </summary>
    internal static void MergeIntoState(Dictionary<string, object?> dict, IStateContainer state, string prefix = "")
        => SaveDataService.MergeIntoState(dict, state, prefix);

    /// <summary>
    /// 清空所有 _local_ 前缀的局部变量（场景切换时调用）
    /// <para>注意：不再自动清除回溯检查点。检查点的清除由调用方显式控制。</para>
    /// </summary>
    private void ClearLocalVariables()
    {
        var keys = _state.Keys.Where(k => k.StartsWith("_local_")).ToList();
        foreach (var key in keys)
            _state.Remove(key);
    }

    /// <summary>
    /// 场景切换时重置交互状态（对标 Ren'Py scene 命令行为）
    /// <para>清除对话、菜单、输入、过渡、动画等运行时状态</para>
    /// </summary>
    private void ResetInteractionState()
    {
        // 对话相关
        _state.Set(StateKeys.Dialog.Text, "");
        _state.Set(StateKeys.Dialog.Speaker, "");
        _state.Set(StateKeys.Dialog.Complete, false);
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);
        _state.Set(StateKeys.Dialog.SpeakerColor, (string?)null);
        _state.Set(StateKeys.Dialog.TextColor, (string?)null);
        _state.Set(StateKeys.Dialog.SpeakerFont, (string?)null);
        _state.Set(StateKeys.Dialog.TextFont, (string?)null);
        _state.Set(StateKeys.Dialog.Clickable, false);
        _state.Set(StateKeys.Dialog.Noskip, false);
        _state.Set(StateKeys.Dialog.Instant, false);
        _state.Set<object?>(StateKeys.Dialog.SideImage, null);
        // Phase 24: 不重置 WindowMode——window hide/show 是显式控制，场景切换不应自动重置

        // 菜单相关
        _state.Set(StateKeys.Menu.Prompt, "");
        _state.Set(StateKeys.Menu.Options, Array.Empty<string>());
        _state.Set(StateKeys.Menu.Selected, -1);

        // 输入相关
        _state.Set(StateKeys.Input.Prompt, "");
        _state.Set(StateKeys.Input.Result, (string?)null);
        _state.Set(StateKeys.Input.Options, Array.Empty<string>());

        // 过渡动画
        _state.Set(StateKeys.Transition.Active, false);
        _state.Set(StateKeys.Transition.Progress, 1.0);
        _state.Set(StateKeys.Transition.OffsetX, 0.0);
        _state.Set(StateKeys.Transition.OffsetY, 0.0);
        _state.Set(StateKeys.Transition.Scale, 1.0);

        // DSL 执行器停止（取消异步执行任务）
        _dslExecutor?.Stop();
        _state.Set(StateKeys.Dsl.Waiting, false);
        _state.Set(StateKeys.Input.DslWaiting, false);
        _state.Set(StateKeys.Menu.DslPrompt, "");
        _state.Set(StateKeys.Menu.DslOptions, Array.Empty<object>());
        _state.Set(StateKeys.Input.DslPrompt, "");
        _state.Set(StateKeys.Input.DslStore, "");

        // 清理运行时元素
        _state.Set(StateKeys.Scene.RuntimeElements, new List<Abstractions.Entities.UIs.UIElementEntity>());
        _state.Set(StateKeys.Scene.Dirty, false);

        // NVL 模式重置
        _state.Set(StateKeys.Nvl.Active, false);
        _state.Set(StateKeys.Nvl.Text, "");
        _state.Set(StateKeys.Nvl.Speakers, "");
        _state.Set(StateKeys.Nvl.Count, 0);

        // 视频停止
        _videoManager?.Stop();

        // 音频生命周期：根据配置标记决定是否自动停止 BGM/Voice
        var options = _options;
        if (ShouldAutoStop(StateKeys.Audio.BgmAutoStop, options.DefaultAutoStopBgm))
            _ = _audioManager?.StopBgmAsync();
        if (ShouldAutoStop(StateKeys.Audio.VoiceAutoStop, options.DefaultAutoStopVoice))
            _audioManager?.StopVoice();
    }

    private bool ShouldAutoStop(string stateKey, bool defaultVal)
    {
        var val = _state.Get<object?>(stateKey);
        if (val is bool b) return b;
        return defaultVal;
    }

    // ========== 性能监控 ==========

    private double _fpsAccumulator;
    private int _fpsFrameCount;
    private double _fpsUpdateTimer;

    /// <summary>
    /// 更新性能监控指标（每帧调用）
    /// </summary>
    private void UpdatePerformanceMetrics(double frameDelta)
    {
        // FPS 计算（滑动平均，每 0.5 秒更新一次）
        _fpsAccumulator += frameDelta;
        _fpsFrameCount++;
        _fpsUpdateTimer += frameDelta;

        if (_fpsUpdateTimer >= 0.5)
        {
            var fps = _fpsFrameCount / _fpsAccumulator;
            _state.Set(StateKeys.Performance.Fps, Math.Round(fps, 1));
            _state.Set(StateKeys.Performance.FrameTimeMs, Math.Round(_fpsAccumulator / _fpsFrameCount * 1000, 2));
            _fpsAccumulator = 0;
            _fpsFrameCount = 0;
            _fpsUpdateTimer = 0;

            // 非 FPS 指标也仅在 0.5s 周期更新——避免每帧 5+ 次 ConcurrentDictionary 写入 + ValueChanged 事件开销
            _state.Set(StateKeys.Performance.CommandQueueDepth, _pipeline.Count);

            var dslIndex = _state.Get<int>(StateKeys.Dsl.CurrentIndex);
            var dslTotal = _state.Get<int>(StateKeys.Dsl.TotalCommands);
            _state.Set(StateKeys.Performance.DslCurrentIndex, dslIndex);
            _state.Set(StateKeys.Performance.DslTotalCommands, dslTotal);

            // 活跃动画数量（仅在 HUD 刷新周期扫描一次）
            var animCount = 0;
            foreach (var k in _state.Keys)
            {
                if (k.StartsWith(StateKeys.Animation.Prefix) && k.EndsWith(StateKeys.Animation.ActiveSuffix))
                    animCount++;
            }
            _state.Set(StateKeys.Performance.ActiveAnimations, animCount);

            // 场景元素数量
            var elements = _state.Get<List<Abstractions.Entities.UIs.UIElementEntity>>(StateKeys.Scene.Elements);
            _state.Set(StateKeys.Performance.SceneElementCount, elements?.Count ?? 0);

            // 回溯检查点数量
            var checkpoints = _state.Get<List<Abstractions.Models.RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
            _state.Set(StateKeys.Performance.CheckpointCount, checkpoints?.Count ?? 0);

            // 托管内存
            var memMb = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            _state.Set(StateKeys.Performance.MemoryMb, Math.Round(memMb, 1));
        }
    }

    // ========== CommandContext — ICommandContext 实现 ==========

    /// <summary>
    /// 命令处理上下文实现 — 将 GameLoop 的依赖和操作暴露给 handler
    /// <para>作为私有嵌套类，可直接访问 GameLoop 的所有成员。</para>
    /// </summary>
    private class CommandContext(GameLoop loop) : ICommandContext
    {
        public IStateContainer State => loop._state;
        public ICommandPipeline Pipeline => loop._pipeline;
        public ISceneRegistry? SceneRegistry => loop._sceneRegistry;
        public ISceneStack? SceneStack => loop._sceneStack;
        public IStoryRegistry? StoryRegistry => loop._storyRegistry;
        public IDslExecutor? DslExecutor => loop._dslExecutor;
        public ITransitionEngine? TransitionEngine => loop._transitionEngine;
        public IAudioManager? AudioManager => loop._audioManager;
        public IVideoManager? VideoManager => loop._videoManager;
public IEventScheduler? EventScheduler => loop._eventScheduler;
public ITimeEventRegistry? TimeEventRegistry => loop._timeEventRegistry;
public IGameTimeService? TimeService => loop._time;
        public ISaveService? SaveService => loop._saveService;
        public LingFanEngineOptions Options => loop._options;
        public Func<byte[]?>? CaptureThumbnail => loop._sceneRenderer == null ? null : () => loop._sceneRenderer.CaptureThumbnail();

        public bool TryGetScriptEntry(string sceneName, out Abstractions.Scripting.SceneScriptEntry? entry)
        {
            bool found = loop._scriptEntries.TryGetValue(sceneName, out var temp);
            entry = temp;
            return found;
        }

        public void ResetInteractionState() => loop.ResetInteractionState();
        public void ClearLocalVariables() => loop.ClearLocalVariables();
        public Abstractions.Models.SaveData? BuildSaveData() => loop._saveDataService.BuildSaveData();
        public void ApplySaveData(Abstractions.Models.SaveData data) => loop._saveDataService.ApplySaveData(data);
        public void ApplySaveData(Abstractions.Models.SaveData data, bool continueGame) => loop._saveDataService.ApplySaveData(data, continueGame);
        public void ReportException(Exception ex, string source) => loop.OnException?.Invoke(ex, source);
    }

    // ========== IDisposable ==========

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 停止主循环
        try { _stopCts?.Cancel(); } catch { }
        try { if (_loopTask != null && !_loopTask.IsCompleted) _loopTask.Wait(2000); } catch { }

        // 释放停止 CTS
        _stopCts?.Dispose();

        // 释放管道（CommandPipeline 实现了 IDisposable，但接口未暴露，用 is 检查）
        if (_pipeline is IDisposable disposablePipeline)
            try { disposablePipeline.Dispose(); } catch { }

        GC.SuppressFinalize(this);
    }
}
