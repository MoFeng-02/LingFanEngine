using Avalonia.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// 视频呈现接口——同步状态键到视频后端，管理视频播放器生命周期（当前默认实现为 <see cref="NullVideoPresenter"/> 空操作）。
/// <para>只依赖 IVideoPlayer 接口，不感知后端具体类型；更换渲染后端（如未来原生+VLC/FFmpeg）时本接口及实现零改动。</para>
/// </summary>
public interface IVideoPresenter
{
    /// <summary>
    /// 场景重建后附加新的视觉树引用
    /// </summary>
    void Attach(Panel? sceneRoot, Grid? outerGrid);

    /// <summary>
    /// 场景清空时分离并清理视频播放器
    /// </summary>
    void Detach();

    /// <summary>
    /// 每帧同步视频状态
    /// </summary>
    void Update();
}
