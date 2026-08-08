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
/// <para>Idle/Opening/Buffering/Error 为新增维度，用于映射 LingFan.Media 的 MediaState（8 态），
/// 便于适配器暴露缓冲与错误态；原有 4 值顺序不变，非破坏性。</para>
/// </summary>
public enum PlaybackState
{
    /// <summary>停止/未播放</summary>
    Stopped,
    /// <summary>播放中</summary>
    Playing,
    /// <summary>已暂停</summary>
    Paused,
    /// <summary>自然播放结束</summary>
    Finished,
    /// <summary>空闲，未加载媒体（映射 LingFan.Media MediaState.Idle）</summary>
    Idle,
    /// <summary>正在打开/初始化（映射 MediaState.Opening）</summary>
    Opening,
    /// <summary>正在缓冲（网络流/预读取，映射 MediaState.Buffering）</summary>
    Buffering,
    /// <summary>错误状态（映射 MediaState.Error）</summary>
    Error
}
