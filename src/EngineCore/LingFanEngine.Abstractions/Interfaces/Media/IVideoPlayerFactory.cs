namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 视频播放器工厂——创建新的逻辑面实例（每个对应渲染后端中的一个独立槽位）。
/// <para>用于 DSL <c>build video</c> 等需要多个视频并存的元素；主视频（状态键驱动）由
/// 单例 <see cref="IVideoPlayer"/> 提供。</para>
/// </summary>
public interface IVideoPlayerFactory
{
    /// <summary>创建一个视频逻辑面。slotId 为空时自动分配唯一槽位。</summary>
    IVideoPlayer Create(string? slotId = null);
}
