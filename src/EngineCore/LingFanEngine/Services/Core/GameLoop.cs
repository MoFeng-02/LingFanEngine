using System.Diagnostics;
using System.Threading.Channels;
using Avalonia.Threading;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Media;
// Router 已移除
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Saves;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Services.Tweens;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 游戏主循环实现（后台线程 + 可配置帧率）
/// <para>帧循环在后台线程运行，通过 Dispatcher.UIThread.Post 触发 SceneView 更新。</para>
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
    private readonly IDslExecutor? _dslExecutor;
    private CancellationTokenSource? _stopCts;
    private Task? _loopTask;
    private int _targetFps = 60;
    private bool _firstFrame = true;
    /// <summary>
    /// 帧回调（由 GameLoop 每帧触发，SceneView.Update 通过此处注册）
    /// </summary>
    private Action<double>? _uiFrameAction;

    /// <summary>场景视图引用（截图用）</summary>
    private LingFanEngine.Views.SceneView? _sceneView;

    /// <summary>场景名→SceneScriptEntry 映射（C# 剧情脚本，SceneType 决定存档/堆栈行为）</summary>
    private readonly Dictionary<string, Abstractions.Scripting.SceneScriptEntry> _scriptEntries = new(StringComparer.OrdinalIgnoreCase);


    private readonly Channel<Func<Task>> _jobChannel = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource _workerCts = new();

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
        IAudioManager? audioManager = null)
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

        _context = new CommandContext(this);
        RegisterDefaultHandlers(defaultHandlers);
        _stateInitializer.Initialize(_state);

        StartBackgroundWorker();
    }


    private void StartBackgroundWorker()
    {
        var reader = _jobChannel.Reader;
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var job in reader.ReadAllAsync(_workerCts.Token))
                {
                    try
                    {
                        await job();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GameLoop] Background job failed: {ex}");
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                /* 正常取消 */
                OnException?.Invoke(ex, nameof(StartBackgroundWorker));
            }
            catch (Exception ex)
            {
                OnException?.Invoke(ex, nameof(StartBackgroundWorker));
            }
        }, _workerCts.Token);
    }

    /// <summary>
    /// 注册场景视图（UI 层引用，非 DI 管理——用于截图功能）
    /// </summary>
    public void SetSceneView(LingFanEngine.Views.SceneView view)
    {
        _sceneView = view;
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
        var targetFrameTicks = _targetFps > 0 ? Stopwatch.Frequency / (long)_targetFps : 0;
        var stopwatch = Stopwatch.StartNew();
        var accumulatedTime = 0.0;
        var lastFrameTime = 0.0;
        const double timeTickInterval = 1.0;

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
                if (_options.EnableTimeSystem)
                {
                    accumulatedTime += _pipeline.TimeScale * frameDelta;
                    while (accumulatedTime >= timeTickInterval)
                    {
                        _time.Tick();
                        accumulatedTime -= timeTickInterval;
                    }
                }

                // 触发帧回调（投递到 UI 线程执行 SceneView.Update）
                if (_uiFrameAction != null)
                {
                    var delta = frameDelta;
                    var priority = _firstFrame ? DispatcherPriority.Normal : DispatcherPriority.Render;
                    _firstFrame = false;
                    Dispatcher.UIThread.Post(() =>
                    {
                        _uiFrameAction(delta);
                    }, priority);
                }

                // 高精度帧率节流（_targetFps=0 时不限帧）
                if (_targetFps > 0)
                {
                    var elapsedTicks = stopwatch.ElapsedTicks - frameStartTicks;
                    var remainingTicks = targetFrameTicks - elapsedTicks;
                    if (remainingTicks > 0)
                    {
                        var spinEnd = stopwatch.ElapsedTicks + remainingTicks;

                        // > 2ms：用 Task.Delay + 自旋补偿
                        if (remainingTicks > Stopwatch.Frequency * 2 / 1000)
                        {
                            var delayMs = (remainingTicks * 1000L) / Stopwatch.Frequency - 1;
                            if (delayMs > 1)
                            {
                                await Task.Delay((int)delayMs, ct);
                            }
                        }

                        // 剩余时间用自旋等待达到精确帧率
                        while (stopwatch.ElapsedTicks < spinEnd && !ct.IsCancellationRequested)
                        {
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
                case ICommandHandler<RollbackToCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<SceneCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<NavToLabelCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<BuildSceneCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ClearStackCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<MergeDefinesCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ShakeCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ToggleSkipCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<ToggleAutoCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<UnlockGalleryCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<DebugLogCommand> h: _dispatcher.Register(h); break;
                case ICommandHandler<NvlCommand> h: _dispatcher.Register(h); break;
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
        public ISaveService? SaveService => loop._saveService;
        public LingFanEngineOptions Options => loop._options;
        public Func<byte[]?>? CaptureThumbnail => loop._sceneView == null ? null : () => loop._sceneView.CaptureThumbnail();

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
        public void ReportException(Exception ex, string source) => loop.OnException?.Invoke(ex, source);
    }
}

/// <summary>
/// 设置变量命令
/// <para>IsDefine=true 表示"仅在变量不存在时设置"，用于 DSL define ... once 语法。</para>
/// </summary>
public readonly record struct SetVariableCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Key { get; init; }
    public object? Value { get; init; }

    /// <summary>
    /// 是否为定义模式（只在键不存在时写入，用于 DSL define 语法）
    /// </summary>
    public bool IsDefine { get; init; }

    public SetVariableCommand() { }
}

/// <summary>
/// 路由导航命令
/// </summary>
public readonly record struct NavigateCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public int? SceneIndex { get; init; }

    /// <summary>
    /// 可选：直接指定场景名称（配合 scene "name" 语法）
    /// </summary>
    public string? SceneName { get; init; }

    /// <summary>
    /// 可选：入口标签名。场景从 story 文件懒加载后从此 label 开始执行
    /// </summary>
    public string? EntryLabel { get; init; }

    public NavigateCommand() { }
}

/// <summary>
/// 播放 BGM 命令
/// </summary>
public readonly record struct PlayBgmCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public float Volume { get; init; } = 1.0f;

    /// <summary>fadein 渐变持续时间（秒），0 = 即时</summary>
    public double FadeIn { get; init; }

    /// <summary>fadeout 渐变持续时间（秒），0 = 即时</summary>
    public double FadeOut { get; init; }

    /// <summary>场景切换时是否自动停止（null=跟随全局配置）。默认 null。</summary>
    public bool? AutoStop { get; init; }

    public PlayBgmCommand() { }
}

/// <summary>
/// 停止 BGM 命令
/// </summary>
public readonly record struct StopBgmCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public StopBgmCommand() { }
}

/// <summary>
/// 播放音效命令（独立通道，不覆盖 BGM）
/// </summary>
public readonly record struct PlaySeCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public float Volume { get; init; } = 1.0f;
    public PlaySeCommand() { }
}

/// <summary>
/// 播放语音命令（独立通道，不覆盖 BGM/SE）
/// </summary>
public readonly record struct PlayVoiceCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public float Volume { get; init; } = 1.0f;

    /// <summary>场景切换时是否自动停止（null=跟随全局配置）。默认 null。</summary>
    public bool? AutoStop { get; init; }

    public PlayVoiceCommand() { }
}

/// <summary>
/// 显示对话命令
/// </summary>
public readonly record struct ShowDialogCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Text { get; init; }
    public string? Speaker { get; init; }

    /// <summary>说话者名字颜色（如 "#FF88FF"）</summary>
    public string? SpeakerColor { get; init; }

    /// <summary>对话文本颜色</summary>
    public string? TextColor { get; init; }

    /// <summary>说话者字体名</summary>
    public string? SpeakerFont { get; init; }

    /// <summary>对话文本字体名</summary>
    public string? TextFont { get; init; }

    /// <summary>打字机效果开关（默认 true）</summary>
    public bool TypewriterEnabled { get; init; } = true;

    /// <summary>对话栏宽度（屏幕百分比，null=全局默认/全宽）</summary>
    public double? DialogPercentW { get; init; }

    /// <summary>对话栏高度（屏幕百分比，null=全局默认/自适应）</summary>
    public double? DialogPercentH { get; init; }

    /// <summary>对话栏左偏移（像素，null=全局默认/0）</summary>
    public double? DialogMarginL { get; init; }

    /// <summary>对话栏底偏移（像素，null=全局默认/0）</summary>
    public double? DialogMarginB { get; init; }

    public ShowDialogCommand() { }
}

/// <summary>
/// 追加对话命令（对标 Ren'Py extend）
/// </summary>
public readonly record struct ExtendDialogCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Append { get; init; }
    public ExtendDialogCommand() { }
}

/// <summary>
/// BGM 交叉淡入队列命令（下一个 BGM 渐出+新 BGM 渐入）
/// </summary>
public readonly record struct BgmQueueCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public float Volume { get; init; } = 1.0f;
    public double CrossFadeDuration { get; init; } = 2.0;
    public BgmQueueCommand() { }
}

/// <summary>
/// 可中断等待命令（对标 Ren'Py pause hard=True）
/// </summary>
public readonly record struct HardPauseCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public HardPauseCommand() { }
}

/// <summary>
/// 清空场景堆栈命令
/// </summary>
public readonly record struct ClearStackCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public ClearStackCommand() { }
}

/// <summary>
/// 深合并变量定义命令（补缺+修类型）
/// </summary>
public readonly record struct MergeDefinesCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required Dictionary<string, object?> Defines { get; init; }
    public MergeDefinesCommand() { }
}
