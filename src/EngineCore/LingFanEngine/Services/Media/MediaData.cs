using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// 视频数据实现
/// </summary>
public class VideoData : IVideoData
{
    public required string Path { get; init; }
    public bool Loop { get; init; }
    public float Volume { get; init; } = 1.0f;
    public int Width { get; init; }
    public int Height { get; init; }
    public double Duration { get; init; }
}

/// <summary>
/// 音频数据实现
/// </summary>
public class AudioData : IAudioData
{
    public required string Path { get; init; }
    public string Channel { get; init; } = "default";
    public bool Loop { get; init; }
    public float Volume { get; init; } = 1.0f;
    public double Duration { get; init; }
}