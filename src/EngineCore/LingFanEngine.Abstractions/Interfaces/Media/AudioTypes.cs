namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 音频通道类型
/// </summary>
public enum AudioChannel
{
    /// <summary>背景音乐（多轨叠加循环）</summary>
    Bgm,
    /// <summary>音效（短促可重叠）</summary>
    Se,
    /// <summary>语音（单轨独占）</summary>
    Voice
}

/// <summary>
/// 播放状态（通用，音频/视频共用）
/// </summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
    Finished
}
