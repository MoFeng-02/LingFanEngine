using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Core;

/// <summary>
/// CG鉴赏服务实现
/// <para>所有 CG 解锁数据存储在状态容器中（__gallery_unlocked），</para>
/// <para>通过 SaveSystemState 持久化。UI 层通过 StateKeys.Gallery 读取渲染鉴赏界面。</para>
/// </summary>
public class GalleryService : IGalleryService
{
    private readonly IStateContainer _state;

    public GalleryService(IStateContainer state)
    {
        _state = state;
        EnsureDefaults();
    }

    /// <summary>确保默认状态</summary>
    private void EnsureDefaults()
    {
        if (!_state.ContainsKey(StateKeys.Gallery.Unlocked))
            _state.Set(StateKeys.Gallery.Unlocked, new List<GalleryEntry>());
        if (!_state.ContainsKey(StateKeys.Gallery.Visible))
            _state.Set(StateKeys.Gallery.Visible, false);
    }

    /// <inheritdoc/>
    public void Unlock(string id, string imagePath, string? title = null, string? sceneName = null)
    {
        var list = _state.Get<List<GalleryEntry>>(StateKeys.Gallery.Unlocked) ?? [];

        // 已解锁则跳过（幂等）
        if (list.Any(e => e.Id == id))
            return;

        list.Add(new GalleryEntry
        {
            Id = id,
            ImagePath = imagePath,
            Title = title,
            SceneName = sceneName
        });

        _state.Set(StateKeys.Gallery.Unlocked, list);
    }

    /// <inheritdoc/>
    public bool IsUnlocked(string id)
    {
        var list = _state.Get<List<GalleryEntry>>(StateKeys.Gallery.Unlocked) ?? [];
        return list.Any(e => e.Id == id);
    }

    /// <inheritdoc/>
    public List<GalleryEntry> GetUnlocked() =>
        _state.Get<List<GalleryEntry>>(StateKeys.Gallery.Unlocked) ?? [];

    /// <inheritdoc/>
    public void Clear()
    {
        _state.Set(StateKeys.Gallery.Unlocked, new List<GalleryEntry>());
    }

    /// <inheritdoc/>
    public void SetVisible(bool visible) =>
        _state.Set(StateKeys.Gallery.Visible, visible);

    /// <inheritdoc/>
    public bool IsVisible =>
        _state.Get<bool>(StateKeys.Gallery.Visible);
}
