namespace LingFanEngine.Views;

/// <summary>
/// 场景渲染器接口——GameLoop 通过此接口驱动场景渲染，解耦具体实现。
/// </summary>
public interface ISceneRenderer
{
    /// <summary>每帧更新场景（由 GameLoop.OnFrame 调用）</summary>
    void Update(double delta);

    /// <summary>截取场景缩略图（用于存档）</summary>
    byte[]? CaptureThumbnail(int width = 320, int height = 180);
}
