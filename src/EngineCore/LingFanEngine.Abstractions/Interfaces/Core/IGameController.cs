using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 游戏控制器接口 — C# 端主命令 API
/// <para>fire-and-forget 版直接投递命令到管道（不等待）。</para>
/// <para>Async 版等待命令执行完成后返回。</para>
/// </summary>
public interface IGameController
{
    // ========== 导航 ==========
    void Navigate(string sceneName);
    Task NavigateAsync(string sceneName);

    // ========== 对话 ==========
    void Say(string text, string? speaker = null,
        string? speakerColor = null, string? textColor = null,
        bool typewriter = true,
        double? wPct = null, double? hPct = null, double? marginL = null, double? marginB = null);
    Task SayAsync(string text, string? speaker = null,
        string? speakerColor = null, string? textColor = null,
        bool typewriter = true,
        double? wPct = null, double? hPct = null, double? marginL = null, double? marginB = null);

    /// <summary>通知某个等待键已完成（由 UI 层点击时调用，零延迟唤醒）</summary>
    void SignalComplete(string key);

    /// <summary>追加文本到当前对话（对标 Ren'Py extend）</summary>
    Task ExtendDialogAsync(string append);

    // ========== 变量 ==========
    void Set(string key, object? value);
    Task SetAsync(string key, object? value);
    void Define(string key, object? value);
    Task DefineAsync(string key, object? value);

    // ========== 过渡 ==========
    void Transition(string type, double duration = 0.5);
    Task TransitionAsync(string type, double duration = 0.5);

    // ========== 等待 ==========
    void Wait(double seconds);
    Task WaitAsync(double seconds);
    Task HardPauseAsync();

    // ========== 音频 ==========
    void PlayBgm(string path, float volume = 0.8f, double fadeIn = 0, bool? autoStop = null);
    Task PlayBgmAsync(string path, float volume = 0.8f, double fadeIn = 0, bool? autoStop = null);
    void StopBgm(double fadeOut = 0);
    Task StopBgmAsync(double fadeOut = 0);

    // ========== 场景元素 ==========
    void Show(string target, double x = 0, double y = 0);
    Task ShowAsync(string target, double x = 0, double y = 0);
    void Hide(string target);
    Task HideAsync(string target);
    void Background(string path);
    Task BackgroundAsync(string path);

    // ========== 菜单 ==========
    Task<int> ShowMenuAsync(string prompt, string[] options);

    // ========== 用户输入 ==========
    Task<string?> InputAsync(string prompt, string[]? options = null);

    // ========== 音效 ==========
    void PlaySe(string path, float volume = 0.6f);
    Task PlaySeAsync(string path, float volume = 0.6f);
    void StopSe();
    Task StopSeAsync();
    void PlayVoice(string path, float volume = 1.0f, bool? autoStop = null);
    Task PlayVoiceAsync(string path, float volume = 1.0f, bool? autoStop = null);
    void StopVoice();
    Task StopVoiceAsync();

    // ========== 堆栈 ==========
    void Back();
    Task BackAsync();
    void Forward();
    Task ForwardAsync();

    // ========== 存档 ==========
    void Save(string slot);
    Task SaveAsync(string slot);
    void Load(string slot);
    Task LoadAsync(string slot);
    void ClearStack();
    Task ClearStackAsync();
    void MergeDefSets(Dictionary<string, object?> dict);
    Task MergeDefSetsAsync(Dictionary<string, object?> dict);

    // ========== 屏幕震动 ==========
    void Shake(double intensity = 10.0, double duration = 0.5);
    Task ShakeAsync(double intensity = 10.0, double duration = 0.5);

    // ========== 跳过/自动模式 ==========
    void ToggleSkip();
    Task ToggleSkipAsync();
    void ToggleAuto();
    Task ToggleAutoAsync();
    void SetAutoDelay(double delay);

    // ========== 对话历史 ==========
    void ToggleHistory();
    void ClearHistory();
    List<DialogHistoryEntry> GetHistory();

    // ========== 偏好设置 ==========
    void SetVolume(string channel, float volume);
    float GetVolume(string channel);
    void SetMuted(bool muted);
    void SetTextSpeed(double charsPerSecond);

    // ========== CG 鉴赏 ==========
    void UnlockGallery(string id, string imagePath, string? title = null, string? sceneName = null);
    Task UnlockGalleryAsync(string id, string imagePath, string? title = null, string? sceneName = null);
    bool IsGalleryUnlocked(string id);
    List<GalleryEntry> GetGalleryUnlocked();
    void ToggleGallery();

    // ========== 调试控制台 ==========
    void DebugLog(string message, string level = "Info");
    Task DebugLogAsync(string message, string level = "Info");
    List<DebugLogEntry> GetDebugLogs();
    void ClearDebugLogs();
    void SetDebugEnabled(bool enabled);
    void ToggleDebugConsole();

    // ========== NVL 模式 ==========
    void EnterNvl();
    Task EnterNvlAsync();
    void ClearNvl();
    Task ClearNvlAsync();
    bool IsNvlActive { get; }
    string GetNvlText();
    string GetNvlSpeakers();
}
