using Avalonia.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// 覆盖层渲染接口——管理菜单/输入/通知/性能HUD/对话遮罩等覆盖在场景之上的 UI 层。
/// </summary>
public interface IOverlayRenderer
{
    /// <summary>
    /// 场景重建后附加新的视觉树引用
    /// </summary>
    /// <param name="sceneRoot">场景根容器（场景元素 + 对话框 + 遮罩所在面板）</param>
    /// <param name="outerGrid">最外层 Grid（覆盖层所在的容器）</param>
    /// <param name="dialogMask">对话模态遮罩（可为 null）</param>
    void Attach(Panel? sceneRoot, Grid? outerGrid, Border? dialogMask);

    /// <summary>
    /// 场景清空时分离视觉树引用，清理内部状态
    /// </summary>
    void Detach();

    /// <summary>
    /// 每帧更新所有覆盖层
    /// </summary>
    void Update(double delta);
}
