namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 视频播放器逻辑面抽象接口（后端无关，适配 seam）。
/// <para>每个 <see cref="IVideoPlayer"/> 实例是一个逻辑播放面，对应 LingFan.Media 的一个 <c>IMediaPlayer</c> 会话；渲染由具体实现负责。</para>
/// <para>音频归属：视频自带音轨由底层媒体后端（LingFan.Media 的 <c>IMediaPlayer</c>）自身音频管线输出，引擎不再内部处理媒体。
/// 游戏 BGM/SE/Voice/Ambient 也由引擎媒体 seam 适配到 LingFan.Media（见 <see cref="IAudioManager"/>），两套经同一适配器层接入。</para>
/// <para>当前全平台为 NullVideoPlayer 空操作；未来新增 <see cref="IVideoPlayer"/> 实现（LingFan.Media 适配器）并替换 DI 注册即可，上层零改动。</para>
/// </summary>
public interface IVideoPlayer : IAsyncDisposable
{
    /// <summary>加载视频（uri 可为 file:// 临时文件或 http(s) 地址）。</summary>
    Task LoadAsync(string uri, CancellationToken ct = default);

    /// <summary>开始播放，返回的 Task 在播放自然结束（或停止/取消）时完成。</summary>
    Task PlayAsync(CancellationToken ct = default);

    /// <summary>暂停播放</summary>
    Task PauseAsync(CancellationToken ct = default);

    /// <summary>从暂停位置继续播放（复用当前播放会话）</summary>
    Task ResumeAsync(CancellationToken ct = default);

    /// <summary>停止播放并释放</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>跳转到指定位置（Playing/Paused 有效）</summary>
    Task SeekAsync(TimeSpan position, CancellationToken ct = default);

    /// <summary>获取当前播放位置</summary>
    ValueTask<TimeSpan> GetPositionAsync();

    /// <summary>获取视频总时长</summary>
    ValueTask<TimeSpan> GetDurationAsync();

    /// <summary>设置音量 0.0~1.0（视频静音架构下一般置 0，由 AudioManager 接管音频）</summary>
    ValueTask SetVolumeAsync(float volume);

    /// <summary>设置静音</summary>
    ValueTask SetMuteAsync(bool mute);

    /// <summary>获取播放状态</summary>
    ValueTask<PlaybackState> GetStateAsync();

    /// <summary>激活该逻辑面（在渲染后端中显示对应槽位）</summary>
    void Activate();

    /// <summary>停用该逻辑面（隐藏槽位）</summary>
    void Deactivate();

    /// <summary>设置槽位在渲染后端中的层叠次序（背景=0 / 过场=100）</summary>
    void SetZIndex(int z);

    /// <summary>设置槽位在渲染后端中的像素边界（left/top/width/height，相对渲染区域左上角）</summary>
    void SetBounds(double left, double top, double width, double height);
}
