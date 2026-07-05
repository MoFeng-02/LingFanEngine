using System.Diagnostics;
using System.Threading.Channels;
using Avalonia.Threading;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
// Router 已移除
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Media;
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
    private readonly LingFanEngine.Extensions.LingFanEngineOptions _options;
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    private readonly IGameTimeService _time;
    private readonly TweenEngine _tween;
    private readonly ISaveService? _saveService;
    private readonly ISceneRegistry? _sceneRegistry;
    private SceneStack? _sceneStack;
    private StoryRegistry? _storyRegistry;
    private TransitionEngine? _transitionEngine;
    private AudioManager? _audioManager;
    private DslExecutor? _dslExecutor;
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
    private readonly CommandDispatcher _dispatcher = new();
    /// <summary>命令处理上下文（提供给 handler 的依赖 facade）</summary>
    private readonly ICommandContext _context;

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
    /// 构造函数
    /// </summary>
    public GameLoop(ICommandPipeline pipeline, IStateContainer state, IGameTimeService time,
        ISaveService? saveService = null, ISceneRegistry? sceneRegistry = null,
        LingFanEngine.Extensions.LingFanEngineOptions? options = null)
    {
        _pipeline = pipeline;
        _state = state;
        _options = options ?? new LingFanEngine.Extensions.LingFanEngineOptions();
        _time = time;
        _saveService = saveService;
        _sceneRegistry = sceneRegistry;
        _tween = new TweenEngine(state);

        _context = new CommandContext(this);
        RegisterDefaultHandlers();
        InitializeDefaultState();

        StartBackgroundWorker();
    }

    /// <summary>
    /// 初始化新功能的默认状态（对话历史/跳过自动模式/偏好设置/震动）
    /// </summary>
    private void InitializeDefaultState()
    {
        // 对话历史
        if (!_state.ContainsKey(StateKeys.History.Entries))
            _state.Set(StateKeys.History.Entries, new List<DialogHistoryEntry>());
        if (!_state.ContainsKey(StateKeys.History.MaxCount))
            _state.Set(StateKeys.History.MaxCount, 100);
        if (!_state.ContainsKey(StateKeys.History.Visible))
            _state.Set(StateKeys.History.Visible, false);

        // 跳过/自动模式
        if (!_state.ContainsKey(StateKeys.Playback.SkipActive))
            _state.Set(StateKeys.Playback.SkipActive, false);
        if (!_state.ContainsKey(StateKeys.Playback.SkipOnlySeen))
            _state.Set(StateKeys.Playback.SkipOnlySeen, false);
        if (!_state.ContainsKey(StateKeys.Playback.AutoActive))
            _state.Set(StateKeys.Playback.AutoActive, false);
        if (!_state.ContainsKey(StateKeys.Playback.AutoDelay))
            _state.Set(StateKeys.Playback.AutoDelay, 3.0);
        if (!_state.ContainsKey(StateKeys.Playback.AutoTimer))
            _state.Set(StateKeys.Playback.AutoTimer, 0.0);

        // 偏好设置默认值
        if (!_state.ContainsKey(StateKeys.Preferences.MasterVolume))
            _state.Set(StateKeys.Preferences.MasterVolume, 1.0f);
        if (!_state.ContainsKey(StateKeys.Preferences.BgmVolume))
            _state.Set(StateKeys.Preferences.BgmVolume, 0.8f);
        if (!_state.ContainsKey(StateKeys.Preferences.SeVolume))
            _state.Set(StateKeys.Preferences.SeVolume, 0.6f);
        if (!_state.ContainsKey(StateKeys.Preferences.VoiceVolume))
            _state.Set(StateKeys.Preferences.VoiceVolume, 1.0f);
        if (!_state.ContainsKey(StateKeys.Preferences.MasterMuted))
            _state.Set(StateKeys.Preferences.MasterMuted, false);
        if (!_state.ContainsKey(StateKeys.Preferences.TextSpeed))
            _state.Set(StateKeys.Preferences.TextSpeed, 30.0);
        if (!_state.ContainsKey(StateKeys.Preferences.AutoForwardDelay))
            _state.Set(StateKeys.Preferences.AutoForwardDelay, 3.0);
        if (!_state.ContainsKey(StateKeys.Preferences.SkipUnseen))
            _state.Set(StateKeys.Preferences.SkipUnseen, false);
        if (!_state.ContainsKey(StateKeys.Preferences.Fullscreen))
            _state.Set(StateKeys.Preferences.Fullscreen, false);

        // 屏幕震动
        if (!_state.ContainsKey(StateKeys.Shake.Active))
            _state.Set(StateKeys.Shake.Active, false);

        // CG鉴赏
        if (!_state.ContainsKey(StateKeys.Gallery.Unlocked))
            _state.Set(StateKeys.Gallery.Unlocked, new List<GalleryEntry>());
        if (!_state.ContainsKey(StateKeys.Gallery.Visible))
            _state.Set(StateKeys.Gallery.Visible, false);

        // 调试控制台
        if (!_state.ContainsKey(StateKeys.Debug.Logs))
            _state.Set(StateKeys.Debug.Logs, new List<DebugLogEntry>());
        if (!_state.ContainsKey(StateKeys.Debug.Visible))
            _state.Set(StateKeys.Debug.Visible, false);
        if (!_state.ContainsKey(StateKeys.Debug.Enabled))
            _state.Set(StateKeys.Debug.Enabled, false);
        if (!_state.ContainsKey(StateKeys.Debug.MaxLogs))
            _state.Set(StateKeys.Debug.MaxLogs, 500);

        // NVL 模式
        if (!_state.ContainsKey(StateKeys.Nvl.Active))
            _state.Set(StateKeys.Nvl.Active, false);
        if (!_state.ContainsKey(StateKeys.Nvl.Text))
            _state.Set(StateKeys.Nvl.Text, "");
        if (!_state.ContainsKey(StateKeys.Nvl.Speakers))
            _state.Set(StateKeys.Nvl.Speakers, "");
        if (!_state.ContainsKey(StateKeys.Nvl.Count))
            _state.Set(StateKeys.Nvl.Count, 0);

        // 打字机完成标记（供 Skip/Auto 模式检测）
        if (!_state.ContainsKey(StateKeys.Dialog.TypewriterDone))
            _state.Set(StateKeys.Dialog.TypewriterDone, true);
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
    /// 注册过渡引擎（非必需，用于帧更新驱动过渡）
    /// </summary>
    public void SetSceneStack(SceneStack stack)
    {
        _sceneStack = stack;
    }

    public void SetStoryRegistry(StoryRegistry registry)
    {
        _storyRegistry = registry;
    }

    public void SetAudioManager(AudioManager audio) => _audioManager = audio;

    public void SetTransitionEngine(TransitionEngine engine)
    {
        _transitionEngine = engine;
    }

    /// <summary>
    /// 注册 DSL 执行器（非必需，用于 label/jump/menu 运行时执行）
    /// </summary>
    public void SetSceneView(LingFanEngine.Views.SceneView view) => _sceneView = view;

    public void SetDslExecutor(DslExecutor executor)
    {
        _dslExecutor = executor;
    }

    /// <summary>
    /// 补间引擎
    /// </summary>
    public TweenEngine Tween => _tween;

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
                AdvanceAnimations(frameDelta);

                // 屏幕震动推进（每帧更新偏移量）
                AdvanceShake(frameDelta);

                // DSL 执行器单步推进（纯状态驱动，所有状态在状态容器中）
                _dslExecutor?.Step();

                // Step 可能投递了新命令，立即消费
                while (_pipeline.TryRead(out var newCmd))
                {
                    ProcessCommand(newCmd);
                }

                // 跳过/自动模式逻辑（在 DslExecutor.Step 之后，确保读到最新状态）
                ProcessPlaybackModes(frameDelta);

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

    /// <summary>
    /// 每帧推进所有 active 的 animate 动画
    /// </summary>
    private void AdvanceAnimations(double frameDelta)
    {
        // 扫描所有 __anim_*_active 标志，推进 active 的动画
        foreach (var key in _state.Keys)
        {
            if (key is string sk && sk.EndsWith(StateKeys.Animation.ActiveSuffix) && _state.Get<bool>(sk))
            {
                var baseKey = sk[..^7]; // 去掉 StateKeys.Animation.ActiveSuffix
                var elapsed = _state.Get<double>(baseKey + StateKeys.Animation.ElapsedSuffix) + frameDelta;
                var duration = _state.Get<double>(baseKey + StateKeys.Animation.DurationSuffix);
                var easingStr = _state.Get<string>(baseKey + StateKeys.Animation.EasingSuffix) ?? "EaseOutQuad";
                _state.Set(baseKey + StateKeys.Animation.ElapsedSuffix, elapsed);

                if (elapsed >= duration)
                {
                    // 检查是否有剩余循环次数
                    var remaining = _state.Get<int>(baseKey + StateKeys.Animation.RepeatSuffix);
                    if (remaining != 0)
                    {
                        _state.Set(baseKey + StateKeys.Animation.ElapsedSuffix, 0.0);
                        _state.Set(baseKey + StateKeys.Animation.FromSuffix, _state.Get<double>(baseKey + StateKeys.Animation.TargetSuffix));
                        _state.Set(baseKey + StateKeys.Animation.CurrentSuffix, _state.Get<double>(baseKey + StateKeys.Animation.TargetSuffix));
                        if (remaining > 0) _state.Set(baseKey + StateKeys.Animation.RepeatSuffix, remaining - 1);
                    }
                    else
                    {
                        // 动画结束：设为目标值后清理所有 __anim_* 键
                        _state.Set(baseKey + StateKeys.Animation.CurrentSuffix, _state.Get<double>(baseKey + StateKeys.Animation.TargetSuffix));
                        _state.Set(sk, false);
                        _state.Remove(baseKey + StateKeys.Animation.FromSuffix);
                        _state.Remove(baseKey + StateKeys.Animation.TargetSuffix);
                        _state.Remove(baseKey + StateKeys.Animation.DurationSuffix);
                        _state.Remove(baseKey + StateKeys.Animation.EasingSuffix);
                        _state.Remove(baseKey + StateKeys.Animation.ElapsedSuffix);
                        _state.Remove(baseKey + StateKeys.Animation.CurrentSuffix);
                        _state.Remove(baseKey + StateKeys.Animation.RepeatSuffix);
                    }
                }
                else
                {
                    var t = elapsed / duration;
                    var from = _state.Get<double>(baseKey + StateKeys.Animation.FromSuffix);
                    var target = _state.Get<double>(baseKey + StateKeys.Animation.TargetSuffix);
                    var eased = ApplyEasingToAnimation(t, easingStr);
                    _state.Set(baseKey + StateKeys.Animation.CurrentSuffix, from + (target - from) * eased);
                }
            }
        }
    }

    /// <summary>从字符串解析缓动类型并计算 easing 值</summary>
    private static double ApplyEasingToAnimation(double t, string easingStr)
    {
        if (!Enum.TryParse<Abstractions.Interfaces.Core.EasingType>(easingStr, out var easing))
            easing = Abstractions.Interfaces.Core.EasingType.EaseOutQuad;
        return easing switch
        {
            Abstractions.Interfaces.Core.EasingType.Linear => t,
            Abstractions.Interfaces.Core.EasingType.EaseInQuad => t * t,
            Abstractions.Interfaces.Core.EasingType.EaseOutQuad => t * (2 - t),
            Abstractions.Interfaces.Core.EasingType.EaseInOutQuad => t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t,
            Abstractions.Interfaces.Core.EasingType.EaseInCubic => t * t * t,
            Abstractions.Interfaces.Core.EasingType.EaseOutCubic => (t - 1) * (t - 1) * (t - 1) + 1,
            Abstractions.Interfaces.Core.EasingType.EaseInOutCubic => t < 0.5 ? 4 * t * t * t : (t - 1) * (2 * t - 2) * (2 * t - 2) + 1,
            Abstractions.Interfaces.Core.EasingType.EaseInBack => t * t * (2.70158 * t - 1.70158),
            Abstractions.Interfaces.Core.EasingType.EaseOutBack => (t - 1) * (t - 1) * (2.70158 * (t - 1) + 1.70158) + 1,
            Abstractions.Interfaces.Core.EasingType.EaseInOutBack => t < 0.5 ? 0.5 * (t * 2) * (t * 2) * (2.70158 * (t * 2) - 1.70158) : 0.5 * (((t * 2) - 2) * ((t * 2) - 2) * (2.70158 * ((t * 2) - 2) + 1.70158) + 2),
            Abstractions.Interfaces.Core.EasingType.EaseOutBounce => EaseOutBounce(t),
            Abstractions.Interfaces.Core.EasingType.EaseInBounce => 1 - EaseOutBounce(1 - t),
            Abstractions.Interfaces.Core.EasingType.EaseInOutBounce => t < 0.5 ? (1 - EaseOutBounce(1 - 2 * t)) / 2 : (1 + EaseOutBounce(2 * t - 1)) / 2,
            Abstractions.Interfaces.Core.EasingType.EaseInElastic => t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * 2.094395102),
            Abstractions.Interfaces.Core.EasingType.EaseOutElastic => t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * 2.094395102) + 1,
            Abstractions.Interfaces.Core.EasingType.EaseInOutElastic => t == 0 ? 0 : t == 1 ? 1 : t < 0.5 ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * 1.396263402)) / 2 : Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * 1.396263402) / 2 + 1,
            _ => t * (2 - t)
        };
    }

    private static double EaseOutBounce(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;
        if (t < 1 / d1) return n1 * t * t;
        if (t < 2 / d1) { var t2 = t - 1.5 / d1; return n1 * t2 * t2 + 0.75; }
        if (t < 2.5 / d1) { var t3 = t - 2.25 / d1; return n1 * t3 * t3 + 0.9375; }
        var t4 = t - 2.625 / d1;
        return n1 * t4 * t4 + 0.984375;
    }

    private void ProcessCommand(ICommand command)
    {
        _dispatcher.Dispatch(command, _context);
    }

    /// <summary>
    /// 屏幕震动每帧推进——计算随机偏移，到期后归零
    /// </summary>
    private void AdvanceShake(double frameDelta)
    {
        if (!_state.Get<bool>(StateKeys.Shake.Active)) return;

        var elapsed = _state.Get<double>(StateKeys.Shake.Elapsed) + frameDelta;
        var duration = _state.Get<double>(StateKeys.Shake.Duration);
        var intensity = _state.Get<double>(StateKeys.Shake.Intensity);

        if (elapsed >= duration)
        {
            // 震动结束
            _state.Set(StateKeys.Shake.Active, false);
            _state.Set(StateKeys.Shake.OffsetX, 0.0);
            _state.Set(StateKeys.Shake.OffsetY, 0.0);
            _state.Set(StateKeys.Shake.Elapsed, 0.0);
        }
        else
        {
            // 衰减系数：随时间推移震动幅度递减
            var decay = 1.0 - (elapsed / duration);
            var currentIntensity = intensity * decay;
            // 随机偏移（使用简单的伪随机）
            var rng = Random.Shared;
            _state.Set(StateKeys.Shake.OffsetX, (rng.NextDouble() * 2 - 1) * currentIntensity);
            _state.Set(StateKeys.Shake.OffsetY, (rng.NextDouble() * 2 - 1) * currentIntensity);
            _state.Set(StateKeys.Shake.Elapsed, elapsed);
        }
    }

    /// <summary>
    /// 跳过/自动模式处理——在 DslExecutor.Step 之后调用
    /// <para>Skip：对话等待中时立即设置 dialog_complete=true</para>
    /// <para>Auto：对话等待中时累计计时器，达到延迟后设置 dialog_complete=true</para>
    /// </summary>
    private void ProcessPlaybackModes(double frameDelta)
    {
        var waitingType = _state.Get<string>(StateKeys.Dsl.WaitingType);
        if (waitingType != "dialog") return;

        // 回溯模式下不自动推进（用户在浏览历史，不应被跳过/自动模式打断）
        if (_state.Get<bool>(StateKeys.Rollback.IsActive)) return;

        // 对话已完成（用户点击或打字机结束）时不处理
        if (_state.Get<bool>(StateKeys.Dialog.Complete)) return;

        // 检查打字机是否还在进行中（由 SceneView 控制 __typewriter_done）
        // 如果打字机未完成，跳过模式和自动模式都应等待
        var typewriterDone = _state.Get<bool>(StateKeys.Dialog.TypewriterDone);
        if (!typewriterDone) return;

        // 跳过模式：立即推进
        if (_state.Get<bool>(StateKeys.Playback.SkipActive))
        {
            _state.Set(StateKeys.Dialog.Complete, true);
            return;
        }

        // 自动模式：累计计时器，达到延迟后推进
        if (_state.Get<bool>(StateKeys.Playback.AutoActive))
        {
            var timer = _state.Get<double>(StateKeys.Playback.AutoTimer) + frameDelta;
            var delay = _state.Get<double>(StateKeys.Playback.AutoDelay);
            if (delay <= 0) delay = 3.0; // 默认 3 秒

            if (timer >= delay)
            {
                _state.Set(StateKeys.Dialog.Complete, true);
                _state.Set(StateKeys.Playback.AutoTimer, 0.0);
            }
            else
            {
                _state.Set(StateKeys.Playback.AutoTimer, timer);
            }
        }
    }

    /// <summary>
    /// 注册引擎内置的 22 种命令处理器
    /// </summary>
    private void RegisterDefaultHandlers()
    {
        _dispatcher.Register<SetVariableCommand>(new SetVariableHandler());
        _dispatcher.Register<ShowDialogCommand>(new ShowDialogHandler());
        _dispatcher.Register<ExtendDialogCommand>(new ExtendDialogHandler());
        _dispatcher.Register<PlayBgmCommand>(new PlayBgmHandler());
        _dispatcher.Register<PlaySeCommand>(new PlaySeHandler());
        _dispatcher.Register<PlayVoiceCommand>(new PlayVoiceHandler());
        _dispatcher.Register<BgmQueueCommand>(new BgmQueueHandler());
        _dispatcher.Register<TransitionCommand>(new TransitionHandler());
        _dispatcher.Register<AnimateCommand>(new AnimateHandler());
        _dispatcher.Register<ShowHideCommand>(new ShowHideHandler());
        _dispatcher.Register<NavigateCommand>(new NavigateHandler());
        _dispatcher.Register<SaveLoadCommand>(new SaveLoadHandler());
        _dispatcher.Register<InputCommand>(new InputHandler());
        _dispatcher.Register<WaitCommand>(new WaitHandler());
        _dispatcher.Register<HardPauseCommand>(new HardPauseHandler());
        _dispatcher.Register<BackCommand>(new BackHandler());
        _dispatcher.Register<ForwardCommand>(new ForwardHandler());
        _dispatcher.Register<SceneCommand>(new SceneHandler());
        _dispatcher.Register<NavToLabelCommand>(new NavToLabelHandler());
        _dispatcher.Register<BuildSceneCommand>(new BuildSceneHandler());
        _dispatcher.Register<ClearStackCommand>(new ClearStackHandler());
        _dispatcher.Register<MergeDefinesCommand>(new MergeDefinesHandler());
        // 新增：震动/跳过/自动模式处理器
        _dispatcher.Register<ShakeCommand>(new ShakeHandler());
        _dispatcher.Register<ToggleSkipCommand>(new ToggleSkipHandler());
        _dispatcher.Register<ToggleAutoCommand>(new ToggleAutoHandler());
        // 新增：CG鉴赏/调试控制台/NVL模式处理器
        _dispatcher.Register<UnlockGalleryCommand>(new UnlockGalleryHandler());
        _dispatcher.Register<DebugLogCommand>(new DebugLogHandler());
        _dispatcher.Register<NvlCommand>(new NvlHandler());
        // JumpCommand / BranchCommand / CallCommand / ReturnCommand / EvalCommand / EndCommand / MenuCommand
        // 由 DslExecutor 直接处理，无需注册 handler
    }

    /// <summary>
    /// 注册自定义命令处理器（供外部扩展使用）
    /// </summary>
    public void RegisterCommandHandler<TCommand>(ICommandHandler<TCommand> handler)
        where TCommand : ICommand
        => _dispatcher.Register(handler);

    /// <summary>
    /// 从当前状态构建存档数据（全量用户状态 + SceneStack 快照）
    /// </summary>
    private SaveData BuildSaveData()
    {
        var currentSceneName = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";

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
        try { thumb = _sceneView?.CaptureThumbnail(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[GameLoop] CaptureThumbnail failed: {ex.Message}"); }

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
        };

        return data;
    }

    /// <summary>
    /// <summary>
    /// 保存系统偏好（所有非瞬态 __* 变量）到独立文件 __system.sav
    /// <para>保存范围：全部 __* 变量，排除时间系统（__game_time_* 跟游戏存档走）和运行时瞬态变量。</para>
    /// <para>用户自定义 __* 也在保存范围内，自动持久化无需额外配置。</para>
    /// </summary>
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
            System.Diagnostics.Debug.WriteLine($"[GameLoop] SaveSystemState failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载系统偏好（引擎初始化时调用一次）
    /// </summary>
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
                    _state.Set(k, ConvertJsonValue(v));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameLoop] LoadSystemStateAsync failed: {ex.Message}");
        }
    }

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
        // 震动状态是瞬态的（运行时仅）
        or StateKeys.Shake.Active or StateKeys.Shake.Intensity
        or StateKeys.Shake.Duration or StateKeys.Shake.Elapsed
        or StateKeys.Shake.OffsetX or StateKeys.Shake.OffsetY
        // 跳过/自动模式激活状态和计时器是瞬态的
        or StateKeys.Playback.SkipActive or StateKeys.Playback.AutoActive
        or StateKeys.Playback.AutoTimer
        // 历史面板可见性是瞬态的
        or StateKeys.History.Visible
        // 打字机完成标记是瞬态的
        or StateKeys.Dialog.TypewriterDone
        // 鉴赏面板可见性是瞬态的
        or StateKeys.Gallery.Visible
        // 调试面板可见性是瞬态的
        or StateKeys.Debug.Visible
        // NVL 模式状态是瞬态的
        or StateKeys.Nvl.Active or StateKeys.Nvl.Text
        or StateKeys.Nvl.Speakers or StateKeys.Nvl.Count
        => true,
        _ => key.Contains(StateKeys.Animation.Prefix),
    };

    /// 应用存档数据到状态容器（恢复场景 + 堆栈 + 状态）
    /// </summary>
    private void ApplySaveData(SaveData data)
    {
        ResetInteractionState();
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
                        System.Diagnostics.Debug.WriteLine($"[GameLoop] ApplySaveData JSON deserialize failed for key '{k}': {ex.Message}");
                    }
                }
                else
                {
                    _state.Set(k, ConvertJsonValue(v));
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

        // 4a. 尝试 C# StoryScript 场景（优先）
        if (_scriptEntries.TryGetValue(sceneName, out var scriptEntry))
        {
            // 合并场景定义（仅补缺，状态已从存档恢复）
            if (scriptEntry.Defines != null)
                MergeIntoState(scriptEntry.Defines, _state);
            // 重新执行场景脚本（状态已恢复，脚本会根据当前状态走正确分支）
            _ = scriptEntry.Runner();
            System.Diagnostics.Debug.WriteLine($"[GameLoop] ApplySaveData: 重新执行 StoryScript [{sceneName}]");
        }
        // 4b. 尝试 DSL 场景（从存档位置恢复执行）
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
                    if ((waitingType == "dialog" || waitingType == "menu" || waitingType == "input")
                        && savedIndex > 0)
                        savedIndex--;
                    _state.Set(StateKeys.Dsl.CurrentIndex, savedIndex);
                    _state.Set(StateKeys.Dsl.Executing, true);
                    _state.Set(StateKeys.Dsl.WaitingType, "");
                    _state.Set(StateKeys.Dsl.WaitingValue, (object?)null);
                    System.Diagnostics.Debug.WriteLine(
                        $"[GameLoop] ApplySaveData: 恢复 DSL 场景 [{sceneName}] 从索引 {savedIndex}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GameLoop] ApplySaveData: 无法加载 DSL 场景 [{sceneName}]");
            }
        }
        // 4c. 尝试 SceneRegistry 中的场景实体
        else
        {
            var entity = _sceneRegistry?.FindScene(sceneName);
            if (entity != null)
                _state.Set(StateKeys.Scene.Elements, entity.Elements);
            // C# StoryScript 场景不在 SceneRegistry 中——保留存档恢复的 __scene_elements
        }
    }

    /// <summary>
    /// 注册自定义 JsonElement → .NET 类型转换器。
    /// <para>注册的转换器插入默认数字链之前，优先级最高。返回 null 表示不处理，继续走链路。</para>
    /// </summary>
    public static void RegisterJsonConverter(Func<System.Text.Json.JsonElement, object?> converter)
    {
        s_jsonConverters.Insert(0, converter);
    }

    private static readonly List<Func<System.Text.Json.JsonElement, object?>> s_jsonConverters =
    [
        je => je.TryGetInt16(out var s) ? s : null,
        je => je.TryGetInt32(out var i) ? i : null,
        je => je.TryGetInt64(out var l) ? l : null,
        je => je.TryGetSingle(out var f) ? f : null,
        je => je.TryGetDouble(out var d) ? d : null,
        je => je.TryGetDecimal(out var d) ? d : null,
        je => je.TryGetGuid(out var guid) ? guid : null,
        je => je.TryGetDateTimeOffset(out var t) ? t : null,
        je => je.TryGetDateTime(out var t) ? t : null,
        je => je.TryGetByte(out var t) ? t : null,
        // 可选自扩展——开发者需要时自行 RegisterJsonConverter
    ];

    /// <summary>
    /// 将存档反序列化后的 object 值（可能是 JsonElement）转换为 .NET 原生类型
    /// </summary>
    private static object? ConvertJsonValue(object? value)
    {
        if (value is System.Text.Json.JsonElement je)
        {
            return je.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => s_jsonConverters
                    .Select(f => f(je))
                    .FirstOrDefault(r => r != null) ?? je.GetDecimal(),  // 改用 Decimal，精度更高
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.String => je.GetString(),
                System.Text.Json.JsonValueKind.Null => null,
                System.Text.Json.JsonValueKind.Array => je.EnumerateArray().Select(je2 => ConvertJsonValue(je2)).ToList(),
                System.Text.Json.JsonValueKind.Object => je.EnumerateObject()
                    .ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value)),
                _ => value
            };
        }
        return value;
    }

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
    private static object? FromSaveEntry(SaveEntry entry)
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
                SaveEntryTypes.DictStringObject => ConvertJsonValue(je),
                _ => ConvertJsonValue(je)
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
            System.Diagnostics.Debug.WriteLine($"[GameLoop] TryDeserializeUIElements failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 深合并场景变量定义到状态容器（仅补缺不覆盖，标量类型不匹配时修复为定义默认值）
    /// <para>支持 Dictionary&lt;string, object?&gt; 和 Dictionary&lt;string, object&gt; 等 IDictionary 实现。</para>
    /// <para>递归合并嵌套字典：若已存在同名字典则深入补缺，否则整体写入。</para>
    /// </summary>
    internal static void MergeIntoState(Dictionary<string, object?> dict, IStateContainer state, string prefix = "")
    {
        foreach (var (k, v) in dict)
        {
            var key = string.IsNullOrEmpty(prefix) ? k : $"{prefix}.{k}";

            // 统一处理所有字典类型（Dict<string,object?>、Dict<string,object> 等）
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
    /// 清空所有 _local_ 前缀的局部变量（场景切换时调用）
    /// </summary>
    private void ClearLocalVariables()
    {
        var keys = _state.Keys.Where(k => k.StartsWith("_local_")).ToList();
        foreach (var key in keys)
            _state.Remove(key);

        // 场景切换时清除回溯检查点（新场景从零开始积累）
        _dslExecutor?.ClearCheckpoints();
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

        // DSL 执行器停止
        _state.Set(StateKeys.Dsl.Executing, false);
        _state.Set(StateKeys.Dsl.Waiting, false);
        _state.Set(StateKeys.Input.DslWaiting, false);
        _state.Set(StateKeys.Dsl.WaitingType, (string?)null);
        _state.Set(StateKeys.Dsl.WaitingValue, (string?)null);
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
        public SceneStack? SceneStack => loop._sceneStack;
        public StoryRegistry? StoryRegistry => loop._storyRegistry;
        public DslExecutor? DslExecutor => loop._dslExecutor;
        public TransitionEngine? TransitionEngine => loop._transitionEngine;
        public AudioManager? AudioManager => loop._audioManager;
        public ISaveService? SaveService => loop._saveService;
        public LingFanEngine.Extensions.LingFanEngineOptions Options => loop._options;
        public Func<byte[]?>? CaptureThumbnail => loop._sceneView == null ? null : () => loop._sceneView.CaptureThumbnail();

        public bool TryGetScriptEntry(string sceneName, out Abstractions.Scripting.SceneScriptEntry? entry)
        {
            bool found = loop._scriptEntries.TryGetValue(sceneName, out var temp);
            entry = temp;
            return found;
        }

        public void ResetInteractionState() => loop.ResetInteractionState();
        public void ClearLocalVariables() => loop.ClearLocalVariables();
        public Abstractions.Models.SaveData BuildSaveData() => loop.BuildSaveData();
        public void ApplySaveData(Abstractions.Models.SaveData data) => loop.ApplySaveData(data);
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
