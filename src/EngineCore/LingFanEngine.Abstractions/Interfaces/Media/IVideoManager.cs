namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 视频管理器接口
/// <para>管理视频播放生命周期，通过 StateContainer 状态键驱动 SceneView 中的 GpuMediaPlayer 控件。</para>
/// <para>VideoManager 不直接持有 UI 控件——遵循"List 和 Dict 驱动一切"的引擎哲学。</para>
/// </summary>
public interface IVideoManager
{
    /// <summary>视频音量 (0~1)</summary>
    float Volume { get; set; }

    /// <summary>视频是否已播放结束（只读）</summary>
    bool IsFinished { get; }

    /// <summary>视频播放结束事件（在后台线程触发，调用方需自行切换到 UI 线程）</summary>
    event Action? OnFinished;

    /// <summary>播放视频</summary>
    /// <param name="path">视频文件路径</param>
    /// <param name="volume">音量 (0~1)</param>
    /// <param name="loop">是否循环播放</param>
    /// <param name="autoPlay">是否自动播放</param>
    void Play(string path, float volume = 1.0f, bool loop = false, bool autoPlay = true);

    /// <summary>停止视频</summary>
    void Stop();

    /// <summary>暂停视频</summary>
    void Pause();

    /// <summary>恢复视频播放</summary>
    void Resume();

    /// <summary>跳转到指定位置</summary>
    /// <param name="position">目标位置</param>
    void Seek(TimeSpan position);

    /// <summary>
    /// 播放全屏过场动画
    /// <para>设置过场模式状态键，SceneView 显示全屏遮罩，用户可点击跳过（skipable=true 时）。</para>
    /// <para>阻塞等待由 GameController.PlayCutsceneAsync 实现。</para>
    /// </summary>
    /// <param name="path">视频文件路径</param>
    /// <param name="skipable">用户是否可点击跳过</param>
    /// <param name="volume">音量 (0~1)</param>
    void PlayCutscene(string path, bool skipable = true, float volume = 1.0f);
}
