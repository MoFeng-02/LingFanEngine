using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Logging;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core;

/// <summary>
/// C# 场景回放被回溯/前进取消时抛出，用于彻底终止过期的 Runner 执行。
/// <para>SayAsync/TransitionAsync 等阻塞方法检测到代次过期时抛出此异常，
/// 异常沿 async 调用链传播到 Runner()，终止后续所有同步代码（AddButton/Set 等），
/// 由 OnCSharpSceneReplay / RunScriptEntryWithGeneration 捕获。</para>
/// </summary>
internal sealed class CSharpSceneReplayCancelledException : Exception
{
    public CSharpSceneReplayCancelledException() : base("C# 场景回放已被回溯/前进取消") { }
}

/// <summary>
/// 游戏控制器——C# 端主命令 API
/// <para>fire-and-forget 版直接投递命令到管道（不等待）。</para>
/// <para>Async 版等待命令执行完成后返回。</para>
/// </summary>
public class GameController : IGameController
{
    private readonly LingFanEngine.Abstractions.EngineOptions.LingFanEngineOptions _options;
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    private readonly IAsyncWaitService _waitService;
    private readonly IEventScheduler? _eventScheduler;
    private readonly IEngineLogger _logger;

    /// <summary>
    /// C# 场景回放代次（AsyncLocal——随 async 调用链流动）
    /// <para>由 GameLoop.OnCSharpSceneReplay 在启动 Runner 前设置。</para>
    /// <para>SayAsync/TransitionAsync 等阻塞方法检测 StateKeys.Dsl.CSharpReplayGeneration 与此值不一致时提前返回。</para>
    /// <para>0 = 不在 C# 场景回放上下文中（DSL 执行路径无需检测）。</para>
    /// </summary>
    internal static readonly AsyncLocal<int> CSharpReplayGen = new();

public GameController(ICommandPipeline pipeline, IStateContainer state,
    LingFanEngine.Abstractions.EngineOptions.LingFanEngineOptions? options = null,
    IAsyncWaitService? waitService = null,
    IEventScheduler? eventScheduler = null,
    IEngineLoggerFactory? loggerFactory = null)
{
_pipeline = pipeline;
_state = state;
_options = options ?? new();
_waitService = waitService!;
_eventScheduler = eventScheduler;
_logger = loggerFactory?.Create("GameController") ?? NullEngineLogger.Instance;
}

    // ========== 导航 ==========

    public void Navigate(string sceneName) =>
        _pipeline.SendAsync(new NavigateCommand { Path = sceneName });
    public Task NavigateAsync(string sceneName) =>
        _pipeline.SendAsync(new NavigateCommand { Path = sceneName }).AsTask();

    // ========== 对话 ==========

    /// <summary>投递对话，不等待</summary>
public void Say(string text, string? speaker = null,
string? speakerColor = null, string? textColor = null,
bool typewriter = true,
double? wPct = null, double? hPct = null, double? marginL = null, double? marginB = null,
bool clickable = false, bool noskip = false, string? template = null) =>
_pipeline.SendAsync(new ShowDialogCommand { Text = text, Speaker = speaker,
SpeakerColor = speakerColor, TextColor = textColor,
TypewriterEnabled = typewriter,
DialogPercentW = wPct, DialogPercentH = hPct,
DialogMarginL = marginL, DialogMarginB = marginB,
Clickable = clickable, Noskip = noskip,
Template = template });

    /// <summary>投递对话，等待用户点击后返回</summary>
public async Task SayAsync(string text, string? speaker = null,
string? speakerColor = null, string? textColor = null,
bool typewriter = true,
double? wPct = null, double? hPct = null, double? marginL = null, double? marginB = null,
bool clickable = false, bool noskip = false, string? template = null)
{
// C# 场景回放被回溯/前进取消时，抛异常终止整个 Runner（不只是跳过此调用）
if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

await _pipeline.SendAsync(new ShowDialogCommand { Text = text, Speaker = speaker,
SpeakerColor = speakerColor, TextColor = textColor,
TypewriterEnabled = typewriter,
DialogPercentW = wPct, DialogPercentH = hPct,
DialogMarginL = marginL, DialogMarginB = marginB,
Clickable = clickable, Noskip = noskip,
Template = template });
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);
        await PollUntilTrue(StateKeys.Dialog.WaitingSayComplete, CancellationToken.None);

        // PollUntilTrue 返回后再次检查——回溯可能在等待期间发生
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
    }

    /// <summary>
    /// 检测当前 C# 场景回放是否已被回溯/前进取消
    /// <para>AsyncLocal 代次与 StateContainer 中的代次不一致 = 已过期</para>
    /// <para>AsyncLocal 为 0 = 不在 C# 场景回放上下文中，返回 false</para>
    /// </summary>
    private bool IsCSharpReplayStale()
    {
        var replayGen = CSharpReplayGen.Value;
        if (replayGen == 0) return false; // 不在 C# 场景回放中
        return _state.Get<int>(StateKeys.Dsl.CSharpReplayGeneration) != replayGen;
    }

    /// <summary>等待状态键变为 true（事件驱动，零延迟唤醒）</summary>
    private async Task PollUntilTrue(string key, CancellationToken ct)
    {
        // Fast path
        if (_state.Get<bool>(key))
        {
            _state.Set(key, false);
            return;
        }

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<bool>(key),
                TimeSpan.FromSeconds(_options.BlockingTimeoutSeconds),
                ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning($"PollUntilTrue({key}) 超时({_options.BlockingTimeoutSeconds}s)，强制推进");
        }
        catch (OperationCanceledException)
        {
            // ct 被取消（如 SkipableWaitAsync 中 Task.WhenAny 后 cts.Cancel）——正常返回，避免未观察任务异常
            return;
        }

        _state.Set(key, false);
    }

    /// <summary>追加文本到当前对话（对标 Ren'Py extend）</summary>
    public async Task ExtendDialogAsync(string append)
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
        await _pipeline.SendAsync(new ExtendDialogCommand { Append = append });
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);
        await PollUntilTrue(StateKeys.Dialog.WaitingSayComplete, CancellationToken.None);
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
    }

    /// <inheritdoc/>
public void DefineCharacter(string key, string? name = null,
string? color = null, string? font = null,
string? textColor = null, string? textFont = null,
string? sideImage = null)
{
var props = new Dictionary<string, object?>();
if (name != null) props["name"] = name;
if (color != null) props["color"] = color;
if (font != null) props["font"] = font;
if (textColor != null) props["textcolor"] = textColor;
if (textFont != null) props["textfont"] = textFont;
if (sideImage != null) props["side"] = sideImage;
_state.Set(StateKeys.Characters.Prefix + key, props);
}

    // ========== 变量 ==========

    public void Set(string key, object? value) =>
        _pipeline.SendAsync(new SetVariableCommand { Key = key, Value = value });

    public Task SetAsync(string key, object? value) =>
        _pipeline.SendAsync(new SetVariableCommand { Key = key, Value = value }).AsTask();

    public void Define(string key, object? value) =>
        _pipeline.SendAsync(new SetVariableCommand { Key = key, Value = value, IsDefine = true });

    public Task DefineAsync(string key, object? value) =>
        _pipeline.SendAsync(new SetVariableCommand { Key = key, Value = value, IsDefine = true }).AsTask();

    // ========== 过渡 ==========

    public void Transition(string type, double duration = 0.5) =>
        _pipeline.SendAsync(new TransitionCommand { Type = type, Duration = duration });

    /// <summary>等待过渡完成（__transition_active == false，120 秒超时兜底）</summary>
    public async Task TransitionAsync(string type, double duration = 0.5)
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        await _pipeline.SendAsync(new TransitionCommand { Type = type, Duration = duration });
        _state.Set(StateKeys.Transition.Active, true);

        try
        {
            await _waitService.WaitForAsync(
                () => !_state.Get<bool>(StateKeys.Transition.Active) || IsCSharpReplayStale(),
                TimeSpan.FromSeconds(_options.BlockingTimeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"TransitionAsync 超时({_options.BlockingTimeoutSeconds}s)，强制清除 Active");
            _state.Set(StateKeys.Transition.Active, false);
        }

        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
    }

    // ========== 等待 ==========

    public void Wait(double seconds) =>
        _pipeline.SendAsync(new WaitCommand { Seconds = seconds });

    /// <summary>等待指定时长，不可跳过（对标 Ren'Py pause(delay, hard=True)）</summary>
    public async Task WaitAsync(double seconds, CancellationToken ct = default)
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    /// <summary>
/// 可跳过的定时等待——用户点击可提前结束（对标 Ren'Py pause(delay, hard=False)）
/// <para>并行监听 Task.Delay 和用户点击，任一触发即完成。</para>
    /// </summary>
    public async Task SkipableWaitAsync(double seconds)
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.WaitSkipable);
        _state.Set(StateKeys.Dialog.Text, "");
        _state.Set(StateKeys.Dialog.Speaker, "");
        _state.Set(StateKeys.Dialog.Clickable, false);
        _state.Set(StateKeys.Dialog.Complete, false);
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);

        using var cts = new CancellationTokenSource();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token);
        var clickTask = PollUntilTrue(StateKeys.Dialog.WaitingSayComplete, cts.Token);

        var winner = await Task.WhenAny(delayTask, clickTask);
        cts.Cancel();

        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        _state.Set(StateKeys.Dsl.WaitingType, "");
        _state.Set(StateKeys.Dialog.Clickable, false);
    }

    /// <summary>等待用户点击（对标 Ren'Py pause()）</summary>
    public async Task WaitForClickAsync()
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        _state.Set(StateKeys.Dsl.WaitingType, StateKeys.Dsl.WaitingTypes.Pause);
        _state.Set(StateKeys.Dialog.Text, "");
        _state.Set(StateKeys.Dialog.Speaker, "");
        _state.Set(StateKeys.Dialog.Clickable, false);
        _state.Set(StateKeys.Dialog.Complete, false);
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);

        await PollUntilTrue(StateKeys.Dialog.WaitingSayComplete, CancellationToken.None);

        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        _state.Set(StateKeys.Dsl.WaitingType, "");
    }

    /// <summary>已废弃——请用 WaitForClickAsync</summary>
    [Obsolete("Use WaitForClickAsync instead")]
    public async Task HardPauseAsync()
    {
        await _pipeline.SendAsync(new HardPauseCommand());
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);
        await PollUntilTrue(StateKeys.Dialog.WaitingSayComplete, CancellationToken.None);
    }

    // ========== 音频 ==========

    public void PlayBgm(string path, float volume = 0.8f, double fadeIn = 0, bool? autoStop = null) =>
        _pipeline.SendAsync(new PlayBgmCommand { Path = path, Volume = volume, FadeIn = fadeIn, AutoStop = autoStop });

    public Task PlayBgmAsync(string path, float volume = 0.8f, double fadeIn = 0, bool? autoStop = null) =>
        _pipeline.SendAsync(new PlayBgmCommand { Path = path, Volume = volume, FadeIn = fadeIn, AutoStop = autoStop }).AsTask();

    public void StopBgm(double fadeOut = 0) =>
        _pipeline.SendAsync(new PlayBgmCommand { Path = "", Volume = 0, FadeOut = fadeOut });

    public Task StopBgmAsync(double fadeOut = 0) =>
        _pipeline.SendAsync(new PlayBgmCommand { Path = "", Volume = 0, FadeOut = fadeOut }).AsTask();

    // ========== 场景元素 ==========

    public void Show(string target, double x = 0, double y = 0) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = target, X = x, Y = y, IsShow = true });

    public Task ShowAsync(string target, double x = 0, double y = 0) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = target, X = x, Y = y, IsShow = true }).AsTask();

    public void Hide(string target) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = target, IsShow = false });
    public Task HideAsync(string target) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = target, IsShow = false }).AsTask();

    public void Background(string path) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = path, X = 0, Y = 0, IsShow = true, IsBackground = true });
    public Task BackgroundAsync(string path) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = path, X = 0, Y = 0, IsShow = true, IsBackground = true }).AsTask();

    // ========== 菜单 ==========

    /// <summary>展示菜单选择面板，返回选中索引</summary>
    public async Task<int> ShowMenuAsync(string prompt, string[] options)
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
        // 清除对话框状态——防止上一句 SayAsync 的文本残留在对话框中
        _state.Set(StateKeys.Dialog.Text, "");
        _state.Set(StateKeys.Dialog.Speaker, "");
        _state.Set(StateKeys.Dialog.Clickable, false);
        _state.Set(StateKeys.Dialog.Complete, false);

        _state.Set(StateKeys.Menu.Prompt, prompt);
        _state.Set<object>(StateKeys.Menu.Options, options);
        _state.Set(StateKeys.Menu.Selected, -1);

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<int>(StateKeys.Menu.Selected) >= 0 || IsCSharpReplayStale(),
                TimeSpan.FromSeconds(_options.InteractionTimeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            // 超时
        }

        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        var selected = _state.Get<int>(StateKeys.Menu.Selected);
        _state.Set(StateKeys.Menu.Prompt, "");
        _state.Set<object>(StateKeys.Menu.Options, Array.Empty<string>());
        _state.Set(StateKeys.Menu.Selected, -1);
        return selected >= 0 && selected < options.Length ? selected : -1;
    }

    // ========== 用户输入 ==========

    /// <summary>展示输入框，返回用户输入文本</summary>
    public async Task<string?> InputAsync(string prompt, string[]? options = null)
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
        // 清除对话框状态——防止上一句 SayAsync 的文本残留在对话框中
        _state.Set(StateKeys.Dialog.Text, "");
        _state.Set(StateKeys.Dialog.Speaker, "");
        _state.Set(StateKeys.Dialog.Clickable, false);
        _state.Set(StateKeys.Dialog.Complete, false);

        _state.Set(StateKeys.Input.Prompt, prompt);
        _state.Set<object>(StateKeys.Input.Options, options ?? Array.Empty<string>());
        _state.Set<object?>(StateKeys.Input.Result, null);

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<string?>(StateKeys.Input.Result) != null || IsCSharpReplayStale(),
                TimeSpan.FromSeconds(_options.InteractionTimeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            // 超时
        }

        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        var result = _state.Get<string?>(StateKeys.Input.Result);
        _state.Set(StateKeys.Input.Prompt, "");
        _state.Set<object>(StateKeys.Input.Options, Array.Empty<string>());
        _state.Set<object?>(StateKeys.Input.Result, null);
        return result;
    }

    // ========== 音效 ==========

    /// <summary>播放音效（独立通道，不影响 BGM）</summary>
    public void PlaySe(string path, float volume = 0.6f) =>
        _pipeline.SendAsync(new PlaySeCommand { Path = path, Volume = volume });

    public Task PlaySeAsync(string path, float volume = 0.6f) =>
        _pipeline.SendAsync(new PlaySeCommand { Path = path, Volume = volume }).AsTask();

public void StopSe() => _pipeline.SendAsync(new PlaySeCommand { Path = "", Volume = 0 });
public Task StopSeAsync() => _pipeline.SendAsync(new PlaySeCommand { Path = "", Volume = 0 }).AsTask();

// DSL 2.0: 环境音
public void PlayAmbient(string path, float volume = 0.8f, bool loop = true) =>
    _pipeline.SendAsync(new PlayAmbientCommand { Path = path, Volume = volume, Loop = loop });

public void StopAmbient() =>
    _pipeline.SendAsync(new StopAmbientCommand());

/// <summary>播放语音（独立通道）</summary>
    public void PlayVoice(string path, float volume = 1.0f, bool? autoStop = null) =>
        _pipeline.SendAsync(new PlayVoiceCommand { Path = path, Volume = volume, AutoStop = autoStop });
    public Task PlayVoiceAsync(string path, float volume = 1.0f, bool? autoStop = null) =>
        _pipeline.SendAsync(new PlayVoiceCommand { Path = path, Volume = volume, AutoStop = autoStop }).AsTask();

    public void StopVoice() => _pipeline.SendAsync(new PlayVoiceCommand { Path = "", Volume = 0 });
    public Task StopVoiceAsync() =>
        _pipeline.SendAsync(new PlayVoiceCommand { Path = "", Volume = 0 }).AsTask();

    // ========== 堆栈 ==========

    public void Back() => _pipeline.SendAsync(new BackCommand());
    public Task BackAsync() => _pipeline.SendAsync(new BackCommand()).AsTask();
    public void Forward() => _pipeline.SendAsync(new ForwardCommand());
    public Task ForwardAsync() => _pipeline.SendAsync(new ForwardCommand()).AsTask();

    // ========== 回溯时间线（Ren'Py 风格）==========
    public void Rollback() => _pipeline.SendAsync(new RollbackCommand());
    public Task RollbackAsync() => _pipeline.SendAsync(new RollbackCommand()).AsTask();
    public void Rollforward() => _pipeline.SendAsync(new RollforwardCommand());
    public Task RollforwardAsync() => _pipeline.SendAsync(new RollforwardCommand()).AsTask();
    public void RollbackTo(int targetCheckpointIndex) => _pipeline.SendAsync(new RollbackToCommand { TargetCheckpointIndex = targetCheckpointIndex });
    public Task RollbackToAsync(int targetCheckpointIndex) =>
        _pipeline.SendAsync(new RollbackToCommand { TargetCheckpointIndex = targetCheckpointIndex }).AsTask();

    // ========== 存档 ==========

    public void Save(string slot) =>
        _pipeline.SendAsync(new SaveLoadCommand { SlotId = slot, IsSave = true });
    public Task SaveAsync(string slot) =>
        _pipeline.SendAsync(new SaveLoadCommand { SlotId = slot, IsSave = true }).AsTask();

    public void Load(string slot) =>
        _pipeline.SendAsync(new SaveLoadCommand { SlotId = slot, IsSave = false });
    public Task LoadAsync(string slot) =>
        _pipeline.SendAsync(new SaveLoadCommand { SlotId = slot, IsSave = false }).AsTask();

    /// <summary>清空场景堆栈（返回主菜单时调用）</summary>
    public void ClearStack() =>
        _pipeline.SendAsync(new ClearStackCommand());
    public Task ClearStackAsync() =>
        _pipeline.SendAsync(new ClearStackCommand()).AsTask();

    /// <summary>重置全部游戏状态（返回主菜单时手动调用）</summary>
    public void ResetGameState() =>
        _pipeline.SendAsync(new ResetGameStateCommand());

    public Task ResetGameStateAsync() =>
        _pipeline.SendAsync(new ResetGameStateCommand()).AsTask();

    /// <summary>深合并变量定义（补缺+修类型）</summary>
    public void MergeDefSets(Dictionary<string, object?> dict) =>
        _pipeline.SendAsync(new MergeDefinesCommand { Defines = dict });
    public Task MergeDefSetsAsync(Dictionary<string, object?> dict) =>
        _pipeline.SendAsync(new MergeDefinesCommand { Defines = dict }).AsTask();

    // ========== 屏幕震动 ==========

    /// <summary>触发屏幕震动</summary>
    public void Shake(double intensity = 10.0, double duration = 0.5) =>
        _pipeline.SendAsync(new ShakeCommand { Intensity = intensity, Duration = duration });

    public Task ShakeAsync(double intensity = 10.0, double duration = 0.5) =>
        _pipeline.SendAsync(new ShakeCommand { Intensity = intensity, Duration = duration }).AsTask();

    // ========== 跳过/自动模式 ==========

    /// <summary>切换跳过模式</summary>
    public void ToggleSkip() =>
        _pipeline.SendAsync(new ToggleSkipCommand());

    public Task ToggleSkipAsync() =>
        _pipeline.SendAsync(new ToggleSkipCommand()).AsTask();

    /// <summary>切换自动模式</summary>
    public void ToggleAuto() =>
        _pipeline.SendAsync(new ToggleAutoCommand());

    public Task ToggleAutoAsync() =>
        _pipeline.SendAsync(new ToggleAutoCommand()).AsTask();

    /// <summary>设置自动模式延迟（秒）</summary>
    public void SetAutoDelay(double delay) =>
        _state.Set(StateKeys.Playback.AutoDelay, delay);

    // ========== 对话历史 ==========

    /// <summary>显示/隐藏对话历史面板</summary>
    public void ToggleHistory()
    {
        var visible = _state.Get<bool>(StateKeys.History.Visible);
        _state.Set(StateKeys.History.Visible, !visible);
    }

    /// <summary>清空对话历史</summary>
    public void ClearHistory()
    {
        _state.Set(StateKeys.History.Entries, new List<DialogHistoryEntry>());
    }

    /// <summary>获取对话历史列表</summary>
    public List<DialogHistoryEntry> GetHistory() =>
        _state.Get<List<DialogHistoryEntry>>(StateKeys.History.Entries) ?? new List<DialogHistoryEntry>();

    // ========== 偏好设置 ==========

    /// <summary>设置音量偏好（0~1）</summary>
    public void SetVolume(string channel, float volume)
    {
        var key = channel.ToLowerInvariant() switch
        {
            "master" => StateKeys.Preferences.MasterVolume,
            "bgm" => StateKeys.Preferences.BgmVolume,
            "se" => StateKeys.Preferences.SeVolume,
            "voice" => StateKeys.Preferences.VoiceVolume,
            _ => StateKeys.Preferences.MasterVolume
        };
        _state.Set(key, Math.Clamp(volume, 0f, 1f));
    }

    /// <summary>获取音量偏好（0~1）</summary>
    public float GetVolume(string channel)
    {
        var key = channel.ToLowerInvariant() switch
        {
            "master" => StateKeys.Preferences.MasterVolume,
            "bgm" => StateKeys.Preferences.BgmVolume,
            "se" => StateKeys.Preferences.SeVolume,
            "voice" => StateKeys.Preferences.VoiceVolume,
            _ => StateKeys.Preferences.MasterVolume
        };
        return _state.Get<float>(key);
    }

    /// <summary>设置静音</summary>
    public void SetMuted(bool muted) =>
        _state.Set(StateKeys.Preferences.MasterMuted, muted);

    /// <summary>设置打字机速度（字符/秒）</summary>
    public void SetTextSpeed(double charsPerSecond) =>
        _state.Set(StateKeys.Preferences.TextSpeed, charsPerSecond);

    // ========== CG 鉴赏 ==========

    /// <summary>解锁 CG</summary>
    public void UnlockGallery(string id, string imagePath, string? title = null, string? sceneName = null) =>
        _pipeline.SendAsync(new UnlockGalleryCommand
        {
            Id = id,
            ImagePath = imagePath,
            Title = title,
            SceneName = sceneName
        });

    /// <summary>解锁 CG（异步）</summary>
    public Task UnlockGalleryAsync(string id, string imagePath, string? title = null, string? sceneName = null) =>
        _pipeline.SendAsync(new UnlockGalleryCommand
        {
            Id = id,
            ImagePath = imagePath,
            Title = title,
            SceneName = sceneName
        }).AsTask();

    /// <summary>检查 CG 是否已解锁</summary>
    public bool IsGalleryUnlocked(string id)
    {
        var list = _state.Get<List<GalleryEntry>>(StateKeys.Gallery.Unlocked) ?? [];
        return list.Any(e => e.Id == id);
    }

    /// <summary>获取已解锁 CG 列表</summary>
    public List<GalleryEntry> GetGalleryUnlocked() =>
        _state.Get<List<GalleryEntry>>(StateKeys.Gallery.Unlocked) ?? [];

    /// <summary>显示/隐藏鉴赏面板</summary>
    public void ToggleGallery()
    {
        var visible = _state.Get<bool>(StateKeys.Gallery.Visible);
        _state.Set(StateKeys.Gallery.Visible, !visible);
    }

    // ========== 调试控制台 ==========

    /// <summary>记录调试日志</summary>
    public void DebugLog(string message, string level = "Info") =>
        _pipeline.SendAsync(new DebugLogCommand { Message = message, Level = level });

    /// <summary>记录调试日志（异步）</summary>
    public Task DebugLogAsync(string message, string level = "Info") =>
        _pipeline.SendAsync(new DebugLogCommand { Message = message, Level = level }).AsTask();

    /// <summary>获取调试日志列表</summary>
    public List<DebugLogEntry> GetDebugLogs() =>
        _state.Get<List<DebugLogEntry>>(StateKeys.Debug.Logs) ?? [];

    /// <summary>清空调试日志</summary>
    public void ClearDebugLogs() =>
        _state.Set(StateKeys.Debug.Logs, new List<DebugLogEntry>());

    /// <summary>开启/关闭调试模式</summary>
    public void SetDebugEnabled(bool enabled) =>
        _state.Set(StateKeys.Debug.Enabled, enabled);

    /// <summary>显示/隐藏调试面板</summary>
    public void ToggleDebugConsole()
    {
        var visible = _state.Get<bool>(StateKeys.Debug.Visible);
        _state.Set(StateKeys.Debug.Visible, !visible);
    }

    // ========== 通知 ==========

    /// <summary>显示通知 Toast</summary>
public void Notify(string text, string type = "info", double duration = 0)
{
var cmd = new NotifyCommand
{
Text = text,
Type = type,
Duration = duration > 0 ? duration : 3.0
};
_pipeline.SendAsync(cmd);
}

    // ========== NVL 模式 ==========

    /// <summary>进入 NVL 模式（后续对话累积显示）</summary>
    public void EnterNvl() =>
        _pipeline.SendAsync(new NvlCommand { IsClear = false });

    /// <summary>进入 NVL 模式（异步）</summary>
    public Task EnterNvlAsync() =>
        _pipeline.SendAsync(new NvlCommand { IsClear = false }).AsTask();

/// <summary>清空 NVL 累积文本（不退出 NVL 模式）</summary>
public void ClearNvl() =>
_pipeline.SendAsync(new NvlCommand { IsClear = true });

/// <summary>清空 NVL 累积文本（不退出 NVL 模式，异步）</summary>
public Task ClearNvlAsync() =>
_pipeline.SendAsync(new NvlCommand { IsClear = true }).AsTask();

/// <summary>退出 NVL 模式并清空累积文本（恢复 ADV 模式）</summary>
public void ExitNvl() =>
_pipeline.SendAsync(new NvlCommand { IsExit = true });

/// <summary>退出 NVL 模式并清空累积文本（恢复 ADV 模式，异步）</summary>
public Task ExitNvlAsync() =>
_pipeline.SendAsync(new NvlCommand { IsExit = true }).AsTask();

    /// <summary>NVL 模式是否激活</summary>
    public bool IsNvlActive =>
        _state.Get<bool>(StateKeys.Nvl.Active);

    /// <summary>获取 NVL 累积文本</summary>
    public string GetNvlText() =>
        _state.Get<string>(StateKeys.Nvl.Text) ?? "";

    /// <summary>获取 NVL 累积说话者列表</summary>
    public string GetNvlSpeakers() =>
        _state.Get<string>(StateKeys.Nvl.Speakers) ?? "";

    // ========== Call Screen ==========
    // Phase 24: CallScreenAsync 带参数版已移至 Phase 24 区域，无参版通过接口默认实现转发

    /// <summary>设置 call_screen 返回结果</summary>
    public void SetScreenResult(string? result)
    {
        _state.Set(StateKeys.ScreenResult, result);
    }

    // ========== 时间事件 ==========

/// <inheritdoc/>
public void RegisterTimeEvent(string target, int triggerDay, int? triggerHour = null,
    int? triggerMinute = null, bool isOneShot = true,
    string? condition = null, string? description = null) =>
    _pipeline.SendAsync(new TimeEventCommand
    {
        Target = target,
        TriggerDay = triggerDay,
        TriggerHour = triggerHour,
        TriggerMinute = triggerMinute,
        IsOneShot = isOneShot,
        Condition = condition,
        Description = description
    });

/// <inheritdoc/>
public void RegisterDailyEvent(string target, int triggerHour, int? triggerMinute = null,
    string? condition = null, string? description = null) =>
    _pipeline.SendAsync(new TimeEventCommand
    {
        Target = target,
        TriggerDay = 0, // 0 = 每日
        TriggerHour = triggerHour,
        TriggerMinute = triggerMinute,
        IsOneShot = false, // 每日重复
        Condition = condition,
        Description = description
    });

/// <inheritdoc/>
public void RegisterWeeklyEvent(string target, DayOfWeek[] daysOfWeek, int triggerHour,
    int? triggerMinute = null, bool isOneShot = false,
    string? condition = null, string? description = null) =>
    _pipeline.SendAsync(new TimeEventCommand
    {
        Target = target,
        TriggerDay = 0, // 使用 DaysOfWeek 而非 TriggerDay
        DaysOfWeek = daysOfWeek,
        TriggerHour = triggerHour,
        TriggerMinute = triggerMinute,
        IsOneShot = isOneShot,
        Condition = condition,
        Description = description
    });

/// <inheritdoc/>
public void SetTimeEventAsync(
    string id,
    int hour,
    Func<Task> callback,
    bool once = false,
    HashSet<DayOfWeek>? weekdays = null,
    int? minute = null,
    int? day = null)
{
    if (_eventScheduler == null)
    {
_logger.LogWarning("IEventScheduler 不可用，无法注册时间事件");
        return;
    }

    _eventScheduler.RegisterEvent(new TimeEventRegistration
    {
        Id = id,
        Hour = hour,
        Minute = minute,
        Day = day,
        Callback = callback,
        IsOneShot = once,
        DaysOfWeek = weekdays?.ToArray()
    });
}

/// <inheritdoc/>
public void UnregisterEvent(string id) =>
    _pipeline.SendAsync(new UnregisterTimeEventCommand { Id = id });

/// <inheritdoc/>
public void UnregisterEvent(string id, bool permanent = false, bool temporary = false)
{
    var mode = permanent ? UnregisterMode.Permanent
              : temporary ? UnregisterMode.Temporary
              : UnregisterMode.Normal;
    _pipeline.SendAsync(new UnregisterTimeEventCommand { Id = id, Mode = mode });
}

/// <inheritdoc/>
public void RestoreEvent(string id) =>
    _pipeline.SendAsync(new RestoreTimeEventCommand { Id = id });

/// <inheritdoc/>
public void PauseGameTime() => _pipeline.SendAsync(new TimePauseCommand());

/// <inheritdoc/>
public void ResumeGameTime() => _pipeline.SendAsync(new TimeResumeCommand());

/// <inheritdoc/>
public void SkipTime(int minutes) => _pipeline.SendAsync(new SkipTimeCommand { Minutes = minutes });

/// <inheritdoc/>
public void ResetGameTime() => _pipeline.SendAsync(new ResetGameStateCommand());

// ========== 视频 ==========

    /// <inheritdoc/>
    public void PlayVideo(string path, float volume = 1.0f, bool loop = false, bool autoPlay = true) =>
        _pipeline.SendAsync(new PlayVideoCommand
        {
            Path = path,
            Volume = volume,
            Loop = loop,
            AutoPlay = autoPlay
        });

    /// <inheritdoc/>
    public Task PlayVideoAsync(string path, float volume = 1.0f, bool loop = false, bool autoPlay = true) =>
        _pipeline.SendAsync(new PlayVideoCommand
        {
            Path = path,
            Volume = volume,
            Loop = loop,
            AutoPlay = autoPlay
        }).AsTask();

    /// <inheritdoc/>
    public void StopVideo() =>
        _pipeline.SendAsync(new StopVideoCommand());

    /// <inheritdoc/>
    public Task StopVideoAsync() =>
        _pipeline.SendAsync(new StopVideoCommand()).AsTask();

    /// <inheritdoc/>
    public void PauseVideo() =>
        _pipeline.SendAsync(new PauseVideoCommand());

    /// <inheritdoc/>
    public Task PauseVideoAsync() =>
        _pipeline.SendAsync(new PauseVideoCommand()).AsTask();

    /// <inheritdoc/>
    public void ResumeVideo() =>
        _pipeline.SendAsync(new ResumeVideoCommand());

    /// <inheritdoc/>
    public Task ResumeVideoAsync() =>
        _pipeline.SendAsync(new ResumeVideoCommand()).AsTask();

    /// <inheritdoc/>
    public void SeekVideo(TimeSpan position) =>
        _pipeline.SendAsync(new SeekVideoCommand { Position = position.TotalSeconds });

    /// <inheritdoc/>
    public Task SeekVideoAsync(TimeSpan position) =>
        _pipeline.SendAsync(new SeekVideoCommand { Position = position.TotalSeconds }).AsTask();

    // ========== 过场动画 ==========

    /// <inheritdoc/>
    public async Task<bool> PlayCutsceneAsync(string path, bool skipable = true, float volume = 1.0f, CancellationToken ct = default)
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
        // 1. 发送过场命令（CutsceneHandler → VideoManager.PlayCutscene → 写状态键）
        await _pipeline.SendAsync(new CutsceneCommand
        {
            Path = path,
            Skipable = skipable,
            Volume = volume
        });

        // 2. 阻塞等待：cutscene 结束（CutsceneActive=false）或用户跳过（CutsceneSkipped=true）
        using var interactionCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.InteractionTimeoutSeconds));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct, interactionCts.Token);

        try
        {
            // 2a. 等待命令被 GameLoop 处理（CutsceneActive 变为 true）
            try
            {
                await _waitService.WaitForAsync(
                    () => _state.Get<bool>(StateKeys.Video.CutsceneActive) || IsCSharpReplayStale(),
                    TimeSpan.FromSeconds(_options.CutsceneActivationTimeoutSeconds),
                    timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 激活超时——跳过等待
            }

            if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

            // 2b. 等待 cutscene 结束或用户跳过
            if (_state.Get<bool>(StateKeys.Video.CutsceneActive))
            {
                await _waitService.WaitForAsync(
                    () => !_state.Get<bool>(StateKeys.Video.CutsceneActive)
                        || _state.Get<bool>(StateKeys.Video.CutsceneSkipped)
                        || IsCSharpReplayStale(),
                    TimeSpan.FromSeconds(_options.InteractionTimeoutSeconds),
                    timeoutCts.Token);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            // 超时兜底
        }

        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        // 3. 清理：停止视频并重置过场状态
        var skipped = _state.Get<bool>(StateKeys.Video.CutsceneSkipped);
        _state.Set(StateKeys.Video.CutsceneActive, false);
        _state.Set(StateKeys.Video.CutsceneSkipped, false);
        _state.Set(StateKeys.Video.IsPlaying, false);
        _state.Set(StateKeys.Video.IsPaused, false);
        _state.Set(StateKeys.Video.IsFinished, false);
        _state.Set(StateKeys.Video.CurrentPath, "");

        return skipped;
    }

    // ========== Phase 24: Ren'Py 功能对齐 ==========

    /// <inheritdoc/>
    public void BlockRollback()
    {
        // 设置为当前 DSL 命令索引——后续检查点 CommandIndex >= 此值则跳过
        var idx = _state.Get<int>(StateKeys.Dsl.CurrentIndex);
        _state.Set(StateKeys.Rollback.BlockedUntil, idx);
    }

    /// <inheritdoc/>
    public void FixRollback()
    {
        _state.Set(StateKeys.Rollback.BlockedUntil, -1);
    }

    /// <inheritdoc/>
    public void ShowWindow()
    {
        _state.Set(StateKeys.Dialog.WindowMode, "show");
    }

    /// <inheritdoc/>
    public void HideWindow()
    {
        _state.Set(StateKeys.Dialog.WindowMode, "hide");
    }

    /// <inheritdoc/>
    public void SetWindowAuto()
    {
        _state.Set(StateKeys.Dialog.WindowMode, "auto");
    }

    /// <inheritdoc/>
    public bool HasScreen(string sceneName)
    {
        var current = _state.Get<string>(StateKeys.Screen.ActiveScreen) ?? "";
        return string.Equals(current, sceneName, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public string? GetCurrentScreen()
    {
        var current = _state.Get<string>(StateKeys.Screen.ActiveScreen);
        return string.IsNullOrEmpty(current) ? null : current;
    }

    /// <inheritdoc/>
    public async Task<string?> CallScreenAsync(string sceneName, CancellationToken ct = default,
        params (string Key, object? Value)[] parameters)
    {
        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();
        // 清除对话框状态——防止上一句 SayAsync 的文本残留在对话框中
        _state.Set(StateKeys.Dialog.Text, "");
        _state.Set(StateKeys.Dialog.Speaker, "");
        _state.Set(StateKeys.Dialog.Clickable, false);
        _state.Set(StateKeys.Dialog.Complete, false);

        // 设置传入参数
        if (parameters.Length > 0)
        {
            var paramDict = new Dictionary<string, object?>();
            foreach (var (key, value) in parameters)
                paramDict[key] = value;
            _state.Set(StateKeys.Screen.Params, paramDict);
        }
        else
        {
            _state.Set<object?>(StateKeys.Screen.Params, null);
        }

        _state.Set<object?>(StateKeys.Screen.Result, null);
        await _pipeline.SendAsync(new NavigateCommand { Path = sceneName });

        try
        {
            await _waitService.WaitForAsync(
                () => _state.Get<string?>(StateKeys.Screen.Result) != null || IsCSharpReplayStale(),
                TimeSpan.FromSeconds(_options.InteractionTimeoutSeconds),
                ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning($"CallScreenAsync 超时({_options.InteractionTimeoutSeconds}s)");
            _state.Set<object?>(StateKeys.Screen.Params, null);
            return null;
        }

        if (IsCSharpReplayStale()) throw new CSharpSceneReplayCancelledException();

        var result = _state.Get<string?>(StateKeys.Screen.Result);
        _state.Set<object?>(StateKeys.Screen.Params, null);
        _state.Set<object?>(StateKeys.Screen.Result, null);
        return result;
    }

    /// <inheritdoc/>
    public T? GetScreenParam<T>(string key)
    {
        var dict = _state.Get<Dictionary<string, object?>>(StateKeys.Screen.Params);
        if (dict == null || !dict.TryGetValue(key, out var val) || val == null) return default;
        if (val is T typed) return typed;
        try { return (T)System.Convert.ChangeType(val, typeof(T)); }
        catch (Exception ex) { _logger.LogError($"GetScreenParam<{typeof(T).Name}> conversion failed for key '{key}'", ex); return default; }
    }
}
