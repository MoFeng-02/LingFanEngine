namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 异步音频播放器抽象接口（基础标准）。
/// <para>所有 I/O 操作异步执行。引擎默认提供 NullAsyncAudioPlayer（空操作）。</para>
/// <para>平台实现者实现此接口接入真实后端（NAudio / FMOD / SDL2-mixer / WebAudio 等）。</para>
/// </summary>
public interface IAudioPlayer : IAsyncDisposable
{
    /// <summary>加载音频文件（解码器初始化 + 首帧缓冲）</summary>
    Task LoadAsync(string source, CancellationToken ct = default);

    /// <summary>开始播放。返回的 Task 在播放自然结束（或 Stop/Cancel 中断）时完成。</summary>
    Task PlayAsync(CancellationToken ct = default);

    /// <summary>暂停播放</summary>
    Task PauseAsync(CancellationToken ct = default);

    /// <summary>
    /// 恢复暂停的播放。
    /// <para>仅在 Paused 状态下有效；Stopped/Finished 状态下调用为空操作。</para>
    /// <para>与 PlayAsync 不同：ResumeAsync 不会创建新的播放会话或重置播放位置，
    /// 而是让已暂停的播放器从暂停位置继续，原 PlayAsync 返回的 Task 继续等待自然结束。</para>
    /// </summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>停止播放并释放解码器资源</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// 跳转到指定位置。
    /// <para>仅在 Playing/Paused 状态下有效；Stopped/Finished 状态应抛出 InvalidOperationException。</para>
    /// </summary>
    Task SeekAsync(TimeSpan position, CancellationToken ct = default);

    /// <summary>获取当前播放位置</summary>
    ValueTask<TimeSpan> GetPositionAsync();

    /// <summary>获取音频总时长</summary>
    ValueTask<TimeSpan> GetDurationAsync();

    /// <summary>设置音量 0.0 ~ 1.0</summary>
    ValueTask SetVolumeAsync(float volume);

    /// <summary>设置静音</summary>
    ValueTask SetMuteAsync(bool mute);

    /// <summary>获取当前播放状态</summary>
    ValueTask<PlaybackState> GetStateAsync();
}
