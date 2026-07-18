namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 视频数据接口
/// <para>服务层返回视频数据，供渲染层播放</para>
/// </summary>
public interface IVideoData
{
    /// <summary>
    /// 视频路径
    /// </summary>
    string Path { get; }

    /// <summary>
    /// 是否循环
    /// </summary>
    bool Loop { get; }

    /// <summary>
    /// 音量 0.0-1.0
    /// </summary>
    float Volume { get; }

    /// <summary>
    /// 视频宽度
    /// </summary>
    int Width { get; }

    /// <summary>
    /// 视频高度
    /// </summary>
    int Height { get; }

    /// <summary>
    /// 视频时长（秒）
    /// </summary>
    double Duration { get; }
}

/// <summary>
/// 音频数据接口
/// <para>服务层返回音频数据，供渲染层播放</para>
/// </summary>
public interface IAudioData
{
    /// <summary>
    /// 音频路径
    /// </summary>
    string Path { get; }

    /// <summary>
    /// 通道名称（如 "bgm"、"se"、"voice"）
    /// </summary>
    string Channel { get; }

    /// <summary>
    /// 是否循环
    /// </summary>
    bool Loop { get; }

    /// <summary>
    /// 音量 0.0-1.0
    /// </summary>
    float Volume { get; }

    /// <summary>
    /// 音频时长（秒）
    /// </summary>
    double Duration { get; }
}

/// <summary>
/// 媒体数据服务接口
/// <para>服务层只负责返回媒体资源的数据和信息，不负责实际播放</para>
/// </summary>
public interface IMediaDataService
{
    /// <summary>
    /// 获取视频数据
    /// </summary>
    /// <param name="path">视频路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>视频数据，不存在返回 null</returns>
    Task<IVideoData?> GetVideoDataAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// 获取音频数据
    /// </summary>
    /// <param name="path">音频路径</param>
    /// <param name="channel">通道名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>音频数据</returns>
    Task<IAudioData> GetAudioDataAsync(string path, string? channel = null, CancellationToken ct = default);

    /// <summary>
    /// 检查资源是否存在
    /// </summary>
    /// <param name="path">资源路径</param>
    Task<bool> ExistsAsync(string path);
}