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
double? wPct = null, double? hPct = null, double? marginL = null, double? marginB = null,
bool clickable = false, bool noskip = false);
Task SayAsync(string text, string? speaker = null,
string? speakerColor = null, string? textColor = null,
bool typewriter = true,
double? wPct = null, double? hPct = null, double? marginL = null, double? marginB = null,
bool clickable = false, bool noskip = false);

    /// <summary>追加文本到当前对话（对标 Ren'Py extend）</summary>
    Task ExtendDialogAsync(string append);

    /// <summary>
    /// 定义角色对话样式（对标 Ren'Py Character 对象）
    /// <para>定义后，Say/SayAsync 的 speaker 匹配此 key 时自动应用样式。</para>
    /// <para>say 显式参数（speakerColor 等）覆盖角色定义。</para>
    /// </summary>
    /// <param name="key">角色标识符（say 的 speaker 匹配此值）</param>
    /// <param name="name">显示名（null=用 key 作为显示名）</param>
    /// <param name="color">说话者名字颜色，如 "#FF4444"</param>
    /// <param name="font">说话者字体</param>
    /// <param name="textColor">对话文本颜色</param>
    /// <param name="textFont">对话文本字体</param>
void DefineCharacter(string key, string? name = null,
string? color = null, string? font = null,
string? textColor = null, string? textFont = null,
string? sideImage = null);

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
    /// <summary>可跳过的定时等待——用户点击可提前结束（对标 Ren'Py pause(delay)）</summary>
    Task SkipableWaitAsync(double seconds);
    /// <summary>等待用户点击（对标 Ren'Py pause()）</summary>
    Task WaitForClickAsync();
    /// <summary>已废弃——请用 WaitForClickAsync</summary>
    [Obsolete("Use WaitForClickAsync instead")]
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

    // ========== 回溯时间线（Ren'Py 风格） ==========
    void Rollback();
    Task RollbackAsync();
    void Rollforward();
    Task RollforwardAsync();

    /// <summary>回溯到指定检查点位置（从历史面板跳转）</summary>
    /// <param name="targetCheckpointIndex">目标检查点索引（DialogHistoryEntry.CheckpointIndex）</param>
    void RollbackTo(int targetCheckpointIndex);
    Task RollbackToAsync(int targetCheckpointIndex);

    // ========== 存档 ==========
    void Save(string slot);
    Task SaveAsync(string slot);
    void Load(string slot);
    Task LoadAsync(string slot);
    void ClearStack();
    Task ClearStackAsync();
    void MergeDefSets(Dictionary<string, object?> dict);
    Task MergeDefSetsAsync(Dictionary<string, object?> dict);

    /// <summary>重置全部游戏状态（返回主菜单时手动调用）</summary>
    void ResetGameState();
    Task ResetGameStateAsync();

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

    // ========== 通知 ==========

    /// <summary>
    /// 显示通知 Toast
    /// </summary>
    /// <param name="text">通知文本</param>
    /// <param name="type">类型："info" / "warning" / "error"</param>
    void Notify(string text, string type = "info");

    // ========== NVL 模式 ==========
    void EnterNvl();
    Task EnterNvlAsync();
    void ClearNvl();
    Task ClearNvlAsync();
    bool IsNvlActive { get; }
    string GetNvlText();
    string GetNvlSpeakers();

    // ========== Call Screen ==========

    /// <summary>
    /// 调用 UI 场景并等待返回结果（对标 Ren'Py call screen）
    /// <para>导航到指定 UI 场景，阻塞等待 SetScreenResult 被调用。</para>
    /// <para>Phase 24: 带参数重载见 Phase 24 区域。</para>
    /// </summary>
    /// <param name="sceneName">UI 场景名</param>
    /// <returns>界面返回结果（由 SetScreenResult 设置）</returns>
    Task<string?> CallScreenAsync(string sceneName, CancellationToken ct = default)
        => CallScreenAsync(sceneName, ct, []);

    /// <summary>
    /// 设置 call_screen 返回结果（UI 场景调用以通知 DslExecutor 继续）
    /// </summary>
    /// <param name="result">返回结果字符串</param>
    void SetScreenResult(string? result);

    // ========== 视频 ==========

    /// <summary>播放视频（fire-and-forget）</summary>
    /// <param name="path">视频文件路径</param>
    /// <param name="volume">音量 (0~1)</param>
    /// <param name="loop">是否循环播放</param>
    /// <param name="autoPlay">是否自动播放</param>
    void PlayVideo(string path, float volume = 1.0f, bool loop = false, bool autoPlay = true);

    /// <summary>播放视频（异步）</summary>
    Task PlayVideoAsync(string path, float volume = 1.0f, bool loop = false, bool autoPlay = true);

    /// <summary>停止视频</summary>
    void StopVideo();

    /// <summary>停止视频（异步）</summary>
    Task StopVideoAsync();

    /// <summary>暂停视频</summary>
    void PauseVideo();

    /// <summary>暂停视频（异步）</summary>
    Task PauseVideoAsync();

    /// <summary>恢复视频播放</summary>
    void ResumeVideo();

    /// <summary>恢复视频播放（异步）</summary>
    Task ResumeVideoAsync();

    /// <summary>跳转视频到指定位置</summary>
    /// <param name="position">目标位置</param>
    void SeekVideo(TimeSpan position);

    /// <summary>跳转视频到指定位置（异步）</summary>
    Task SeekVideoAsync(TimeSpan position);

    /// <summary>
    /// 播放全屏过场动画（阻塞等待播放结束或用户跳过）
    /// <para>对标 Ren'Py renpy.movie_cutscene()。</para>
    /// </summary>
    /// <param name="path">视频文件路径</param>
    /// <param name="skipable">用户是否可点击跳过</param>
    /// <param name="volume">音量 (0~1)</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>true=用户跳过，false=自然结束</returns>
    Task<bool> PlayCutsceneAsync(string path, bool skipable = true, float volume = 1.0f, CancellationToken ct = default);

    // ========== Phase 24: Ren'Py 功能对齐 ==========

    /// <summary>
    /// 阻止回溯——后续检查点不再创建
    /// <para>对标 Ren'Py renpy.block_rollback()</para>
    /// </summary>
    void BlockRollback();

    /// <summary>
    /// 修复回溯——清除回溯阻止标记
    /// <para>对标 Ren'Py renpy.fix_rollback()</para>
    /// </summary>
    void FixRollback();

    /// <summary>
    /// 强制显示对话框窗口
    /// <para>对标 Ren'Py window show</para>
    /// </summary>
    void ShowWindow();

    /// <summary>
    /// 强制隐藏对话框窗口
    /// <para>对标 Ren'Py window hide</para>
    /// </summary>
    void HideWindow();

    /// <summary>
    /// 设置对话框窗口为自动模式（有对话显示、无对话隐藏）
    /// <para>对标 Ren'Py window auto</para>
    /// </summary>
    void SetWindowAuto();

    /// <summary>
    /// 检查指定场景是否正在显示
    /// <para>对标 Ren'Py renpy.has_screen(name)</para>
    /// </summary>
    bool HasScreen(string sceneName);

    /// <summary>
    /// 获取当前显示的场景名
    /// <para>对标 Ren'Py renpy.get_screen(name)</para>
    /// </summary>
    string? GetCurrentScreen();

    /// <summary>
    /// 调用 UI 场景并等待返回（支持传参）
    /// <para>对标 Ren'Py call screen name(args)</para>
    /// </summary>
    /// <param name="sceneName">UI 场景名</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="parameters">传入参数</param>
    Task<string?> CallScreenAsync(string sceneName, CancellationToken ct = default, params (string Key, object? Value)[] parameters);

    /// <summary>
    /// 获取 call_screen 传入的参数值
    /// </summary>
    T? GetScreenParam<T>(string key);
}
