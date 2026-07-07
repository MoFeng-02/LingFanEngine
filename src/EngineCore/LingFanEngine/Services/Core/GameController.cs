using System.Collections.Concurrent;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 游戏控制器——C# 端主命令 API
/// <para>fire-and-forget 版直接投递命令到管道（不等待）。</para>
/// <para>Async 版等待命令执行完成后返回。</para>
/// </summary>
public class GameController : IGameController
{
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    /// <summary>等待表——键名→TaskCompletionSource，供 SignalComplete 零延迟唤醒</summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _waitTable = new();

    public GameController(ICommandPipeline pipeline, IStateContainer state)
    {
        _pipeline = pipeline;
        _state = state;
    }

    // ========== 导航 ==========

    public void Navigate(string sceneName) =>
        _pipeline.SendAsync(new NavigateCommand { Path = sceneName });
    public async Task NavigateAsync(string sceneName)
    { await _pipeline.SendAsync(new NavigateCommand { Path = sceneName }); await Task.Yield(); }

    // ========== 对话 ==========

    /// <summary>投递对话，不等待</summary>
    public void Say(string text, string? speaker = null,
        string? speakerColor = null, string? textColor = null,
        bool typewriter = true,
        double? wPct = null, double? hPct = null, double? marginL = null, double? marginB = null) =>
        _pipeline.SendAsync(new ShowDialogCommand { Text = text, Speaker = speaker,
            SpeakerColor = speakerColor, TextColor = textColor,
            TypewriterEnabled = typewriter,
            DialogPercentW = wPct, DialogPercentH = hPct,
            DialogMarginL = marginL, DialogMarginB = marginB });

    /// <summary>投递对话，等待用户点击后返回</summary>
     public async Task SayAsync(string text, string? speaker = null,
         string? speakerColor = null, string? textColor = null,
         bool typewriter = true,
         double? wPct = null, double? hPct = null, double? marginL = null, double? marginB = null)
     {
         await _pipeline.SendAsync(new ShowDialogCommand { Text = text, Speaker = speaker,
             SpeakerColor = speakerColor, TextColor = textColor,
             TypewriterEnabled = typewriter,
             DialogPercentW = wPct, DialogPercentH = hPct,
             DialogMarginL = marginL, DialogMarginB = marginB });
         _state.Set(StateKeys.Dialog.WaitingSayComplete, false);
         await PollUntilTrue(StateKeys.Dialog.WaitingSayComplete, CancellationToken.None);
     }

    /// <summary>通知某个等待键已完成（由 UI 层点击时调用，零延迟唤醒）</summary>
    public void SignalComplete(string key)
    {
        if (_waitTable.TryRemove(key, out var tcs))
            tcs.TrySetResult(true);
    }

    /// <summary>轮询直到状态标记为 true（120 秒超时）</summary>
    private async Task PollUntilTrue(string key, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>();
        _waitTable[key] = tcs;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        using var registration = linked.Token.Register(() => tcs.TrySetCanceled());

        // 双保险：SignalComplete 零延迟唤醒 + 16ms 轮询回退
        while (!tcs.Task.IsCompleted)
        {
            if (_state.Get<bool>(key))
            {
                tcs.TrySetResult(true);
                break;
            }
            try { await Task.Delay(16, linked.Token); }
            catch (OperationCanceledException) { break; }
        }

        _waitTable.TryRemove(key, out _);
        _state.Set(key, false);
    }

    /// <summary>追加文本到当前对话（对标 Ren'Py extend）</summary>
    public async Task ExtendDialogAsync(string append)
    {
        await _pipeline.SendAsync(new ExtendDialogCommand { Append = append });
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);
        await PollUntilTrue(StateKeys.Dialog.WaitingSayComplete, CancellationToken.None);
    }

    // ========== 变量 ==========

    public void Set(string key, object? value) =>
        _pipeline.SendAsync(new SetVariableCommand { Key = key, Value = value });

    public async Task SetAsync(string key, object? value)
    {
        await _pipeline.SendAsync(new SetVariableCommand { Key = key, Value = value });
        await Task.Yield();
    }

    public void Define(string key, object? value) =>
        _pipeline.SendAsync(new SetVariableCommand { Key = key, Value = value, IsDefine = true });

    public async Task DefineAsync(string key, object? value)
    {
        await _pipeline.SendAsync(new SetVariableCommand { Key = key, Value = value, IsDefine = true });
        await Task.Yield();
    }

    // ========== 过渡 ==========

    public void Transition(string type, double duration = 0.5) =>
        _pipeline.SendAsync(new TransitionCommand { Type = type, Duration = duration });

    /// <summary>等待过渡完成（__transition_active == false）</summary>
    public async Task TransitionAsync(string type, double duration = 0.5)
    {
        await _pipeline.SendAsync(new TransitionCommand { Type = type, Duration = duration });
        _state.Set(StateKeys.Transition.Active, true);
        while (_state.Get<bool>(StateKeys.Transition.Active))
            await Task.Delay(16);
    }

    // ========== 等待 ==========

    public void Wait(double seconds) =>
        _pipeline.SendAsync(new WaitCommand { Seconds = seconds });

    /// <summary>等待指定时长，期间 UI 保持响应</summary>
    public async Task WaitAsync(double seconds) =>
        await Task.Delay((int)(seconds * 1000));

    /// <summary>可中断等待——等待用户点击/按键后返回（对标 Ren'Py pause hard）</summary>
    public async Task HardPauseAsync()
    {
        await _pipeline.SendAsync(new HardPauseCommand());
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);
        await PollUntilTrue(StateKeys.Dialog.WaitingSayComplete, CancellationToken.None);
    }

    // ========== 音频 ==========

    public void PlayBgm(string path, float volume = 0.8f, double fadeIn = 0, bool? autoStop = null) =>
        _pipeline.SendAsync(new PlayBgmCommand { Path = path, Volume = volume, FadeIn = fadeIn, AutoStop = autoStop });

    public async Task PlayBgmAsync(string path, float volume = 0.8f, double fadeIn = 0, bool? autoStop = null)
    {
        await _pipeline.SendAsync(new PlayBgmCommand { Path = path, Volume = volume, FadeIn = fadeIn, AutoStop = autoStop });
        await Task.Yield();
    }

    public void StopBgm(double fadeOut = 0) =>
        _pipeline.SendAsync(new PlayBgmCommand { Path = "", Volume = 0, FadeOut = fadeOut });

    public async Task StopBgmAsync(double fadeOut = 0)
    {
        await _pipeline.SendAsync(new PlayBgmCommand { Path = "", Volume = 0, FadeOut = fadeOut });
        await Task.Yield();
    }

    // ========== 场景元素 ==========

    public void Show(string target, double x = 0, double y = 0) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = target, X = x, Y = y, IsShow = true });

    public async Task ShowAsync(string target, double x = 0, double y = 0)
    { await _pipeline.SendAsync(new ShowHideCommand { Target = target, X = x, Y = y, IsShow = true }); await Task.Yield(); }

    public void Hide(string target) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = target, IsShow = false });
    public async Task HideAsync(string target)
    { await _pipeline.SendAsync(new ShowHideCommand { Target = target, IsShow = false }); await Task.Yield(); }

    public void Background(string path) =>
        _pipeline.SendAsync(new ShowHideCommand { Target = path, X = 0, Y = 0, IsShow = true, IsBackground = true });
    public async Task BackgroundAsync(string path)
    { await _pipeline.SendAsync(new ShowHideCommand { Target = path, X = 0, Y = 0, IsShow = true, IsBackground = true }); await Task.Yield(); }

    // ========== 菜单 ==========

    /// <summary>展示菜单选择面板，返回选中索引</summary>
    public async Task<int> ShowMenuAsync(string prompt, string[] options)
    {
        _state.Set(StateKeys.Menu.Prompt, prompt);
        _state.Set<object>(StateKeys.Menu.Options, options);
        _state.Set(StateKeys.Menu.Selected, -1);
        while (true)
        {
            var selected = _state.Get<int>(StateKeys.Menu.Selected);
            if (selected >= 0 && selected < options.Length)
            {
                _state.Set(StateKeys.Menu.Prompt, "");
                _state.Set(StateKeys.Menu.Options, Array.Empty<string>());
                _state.Set(StateKeys.Menu.Selected, -1);
                return selected;
            }
            await Task.Delay(33);
        }
    }

    // ========== 用户输入 ==========

    /// <summary>展示输入框，返回用户输入文本</summary>
    public async Task<string?> InputAsync(string prompt, string[]? options = null)
    {
        _state.Set(StateKeys.Input.Prompt, prompt);
        _state.Set<object>(StateKeys.Input.Options, options ?? Array.Empty<string>());
        _state.Set<object?>(StateKeys.Input.Result, null);
        while (true)
        {
            var result = _state.Get<string?>(StateKeys.Input.Result);
            if (result != null)
            {
                _state.Set(StateKeys.Input.Prompt, "");
                _state.Set(StateKeys.Input.Options, Array.Empty<string>());
                _state.Set<object?>(StateKeys.Input.Result, null);
                return result;
            }
            await Task.Delay(33);
        }
    }

    // ========== 音效 ==========

    /// <summary>播放音效（独立通道，不影响 BGM）</summary>
    public void PlaySe(string path, float volume = 0.6f) =>
        _pipeline.SendAsync(new PlaySeCommand { Path = path, Volume = volume });

    public async Task PlaySeAsync(string path, float volume = 0.6f)
    {
        await _pipeline.SendAsync(new PlaySeCommand { Path = path, Volume = volume });
        await Task.Yield();
    }

    public void StopSe() => _pipeline.SendAsync(new PlaySeCommand { Path = "", Volume = 0 });
    public async Task StopSeAsync() { await _pipeline.SendAsync(new PlaySeCommand { Path = "", Volume = 0 }); await Task.Yield(); }

    /// <summary>播放语音（独立通道）</summary>
    public void PlayVoice(string path, float volume = 1.0f, bool? autoStop = null) =>
        _pipeline.SendAsync(new PlayVoiceCommand { Path = path, Volume = volume, AutoStop = autoStop });
    public async Task PlayVoiceAsync(string path, float volume = 1.0f, bool? autoStop = null)
    { await _pipeline.SendAsync(new PlayVoiceCommand { Path = path, Volume = volume, AutoStop = autoStop }); await Task.Yield(); }

    public void StopVoice() => _pipeline.SendAsync(new PlayVoiceCommand { Path = "", Volume = 0 });
    public async Task StopVoiceAsync() { await _pipeline.SendAsync(new PlayVoiceCommand { Path = "", Volume = 0 }); await Task.Yield(); }

    // ========== 堆栈 ==========

    public void Back() => _pipeline.SendAsync(new BackCommand());
    public async Task BackAsync() { await _pipeline.SendAsync(new BackCommand()); await Task.Yield(); }
    public void Forward() => _pipeline.SendAsync(new ForwardCommand());
    public async Task ForwardAsync() { await _pipeline.SendAsync(new ForwardCommand()); await Task.Yield(); }

    // ========== 存档 ==========

    public void Save(string slot) =>
        _pipeline.SendAsync(new SaveLoadCommand { SlotId = slot, IsSave = true });
    public async Task SaveAsync(string slot)
    { await _pipeline.SendAsync(new SaveLoadCommand { SlotId = slot, IsSave = true }); await Task.Yield(); }

    public void Load(string slot) =>
        _pipeline.SendAsync(new SaveLoadCommand { SlotId = slot, IsSave = false });
    public async Task LoadAsync(string slot)
    { await _pipeline.SendAsync(new SaveLoadCommand { SlotId = slot, IsSave = false }); await Task.Yield(); }

    /// <summary>清空场景堆栈（返回主菜单时调）</summary>
    public void ClearStack() =>
        _pipeline.SendAsync(new ClearStackCommand());
    public async Task ClearStackAsync()
    { await _pipeline.SendAsync(new ClearStackCommand()); await Task.Yield(); }

    /// <summary>深合并变量定义（补缺+修类型）</summary>
    public void MergeDefSets(Dictionary<string, object?> dict) =>
        _pipeline.SendAsync(new MergeDefinesCommand { Defines = dict });
    public async Task MergeDefSetsAsync(Dictionary<string, object?> dict)
    { await _pipeline.SendAsync(new MergeDefinesCommand { Defines = dict }); await Task.Yield(); }

    // ========== 屏幕震动 ==========

    /// <summary>触发屏幕震动</summary>
    public void Shake(double intensity = 10.0, double duration = 0.5) =>
        _pipeline.SendAsync(new ShakeCommand { Intensity = intensity, Duration = duration });

    public async Task ShakeAsync(double intensity = 10.0, double duration = 0.5)
    { await _pipeline.SendAsync(new ShakeCommand { Intensity = intensity, Duration = duration }); await Task.Yield(); }

    // ========== 跳过/自动模式 ==========

    /// <summary>切换跳过模式</summary>
    public void ToggleSkip() =>
        _pipeline.SendAsync(new ToggleSkipCommand());

    public async Task ToggleSkipAsync()
    { await _pipeline.SendAsync(new ToggleSkipCommand()); await Task.Yield(); }

    /// <summary>切换自动模式</summary>
    public void ToggleAuto() =>
        _pipeline.SendAsync(new ToggleAutoCommand());

    public async Task ToggleAutoAsync()
    { await _pipeline.SendAsync(new ToggleAutoCommand()); await Task.Yield(); }

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
    public async Task UnlockGalleryAsync(string id, string imagePath, string? title = null, string? sceneName = null)
    {
        await _pipeline.SendAsync(new UnlockGalleryCommand
        {
            Id = id,
            ImagePath = imagePath,
            Title = title,
            SceneName = sceneName
        });
        await Task.Yield();
    }

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
    public async Task DebugLogAsync(string message, string level = "Info")
    {
        await _pipeline.SendAsync(new DebugLogCommand { Message = message, Level = level });
        await Task.Yield();
    }

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

    // ========== NVL 模式 ==========

    /// <summary>进入 NVL 模式（后续对话累积显示）</summary>
    public void EnterNvl() =>
        _pipeline.SendAsync(new NvlCommand { IsClear = false });

    /// <summary>进入 NVL 模式（异步）</summary>
    public async Task EnterNvlAsync()
    {
        await _pipeline.SendAsync(new NvlCommand { IsClear = false });
        await Task.Yield();
    }

    /// <summary>清空 NVL 累积文本并退出 NVL 模式</summary>
    public void ClearNvl() =>
        _pipeline.SendAsync(new NvlCommand { IsClear = true });

    /// <summary>清空 NVL 累积文本并退出 NVL 模式（异步）</summary>
    public async Task ClearNvlAsync()
    {
        await _pipeline.SendAsync(new NvlCommand { IsClear = true });
        await Task.Yield();
    }

    /// <summary>NVL 模式是否激活</summary>
    public bool IsNvlActive =>
        _state.Get<bool>(StateKeys.Nvl.Active);

    /// <summary>获取 NVL 累积文本</summary>
    public string GetNvlText() =>
        _state.Get<string>(StateKeys.Nvl.Text) ?? "";

    /// <summary>获取 NVL 累积说话者列表</summary>
    public string GetNvlSpeakers() =>
        _state.Get<string>(StateKeys.Nvl.Speakers) ?? "";

}