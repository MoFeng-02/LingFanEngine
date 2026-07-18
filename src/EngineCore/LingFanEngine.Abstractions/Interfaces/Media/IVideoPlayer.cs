namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 异步视频播放器抽象接口。
/// <para>继承 IAudioPlayer：视频播放器天然具备音频能力。</para>
/// <para>引擎不提供默认实现，开发者需自行接入（LibVLC / FFmpeg / AVPlayer 等）。</para>
/// <para>注意：不应将 IVideoPlayer 实例用作 BGM——AudioManager 管理的是独立 IAudioPlayer。</para>
/// </summary>
public interface IVideoPlayer : IAudioPlayer
{
    /// <summary>获取视频元数据（分辨率、帧率、时长、像素格式）</summary>
    ValueTask<VideoInfo> GetVideoInfoAsync();

    /// <summary>绑定渲染目标（桌面端窗口句柄 / 移动端 Surface）</summary>
    Task BindOutputTargetAsync(IVideoOutputTarget target, CancellationToken ct = default);

    /// <summary>
    /// 帧数据拉流——IAsyncEnumerable 天然支持背压。
    /// UI 渲染慢 → 循环自然阻塞 → 上游不生产多余帧
    /// </summary>
    IAsyncEnumerable<VideoFrame> ReadFramesAsync(CancellationToken ct = default);
}

/// <summary>视频元数据</summary>
public readonly record struct VideoInfo(
    int Width, int Height, double FrameRate, TimeSpan Duration, PixelFormat Format);

/// <summary>视频帧（零拷贝，只传指针）</summary>
public readonly record struct VideoFrame(
    IntPtr DataPtr, int Width, int Height, int Stride,
    PixelFormat Format, TimeSpan Timestamp);

/// <summary>像素格式</summary>
public enum PixelFormat { Unknown, Bgra32, Yuv420p, Nv12 }

/// <summary>视频渲染目标抽象</summary>
public interface IVideoOutputTarget
{
    IntPtr NativeHandle { get; }
}
