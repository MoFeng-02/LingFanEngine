using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// CG鉴赏服务接口
/// <para>负责 CG 解锁、查询和鉴赏面板状态管理。</para>
/// <para>所有数据存储在状态容器中，UI 层通过 StateKeys.Gallery 读取。</para>
/// </summary>
public interface IGalleryService
{
    /// <summary>解锁指定 CG</summary>
    /// <param name="id">CG 唯一标识符</param>
    /// <param name="imagePath">CG 图片路径</param>
    /// <param name="title">CG 标题（可选）</param>
    /// <param name="sceneName">关联场景名（可选，用于回想去该场景）</param>
    void Unlock(string id, string imagePath, string? title = null, string? sceneName = null);

    /// <summary>检查指定 CG 是否已解锁</summary>
    bool IsUnlocked(string id);

    /// <summary>获取所有已解锁 CG 列表</summary>
    List<GalleryEntry> GetUnlocked();

    /// <summary>清空所有已解锁 CG</summary>
    void Clear();

    /// <summary>显示/隐藏鉴赏面板</summary>
    void SetVisible(bool visible);

    /// <summary>获取鉴赏面板可见性</summary>
    bool IsVisible { get; }
}
