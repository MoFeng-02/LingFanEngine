using Avalonia.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// 视频呈现接口——同步状态键到 GpuMediaPlayer 控件，管理视频播放器生命周期。
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
