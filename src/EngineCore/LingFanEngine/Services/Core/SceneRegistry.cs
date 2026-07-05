using System.Collections.Concurrent;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 场景注册表实现
/// <para>管理所有已知场景的注册和查找，供渲染器按场景名获取 UIElement 列表。</para>
/// </summary>
public class SceneRegistry : ISceneRegistry
{
    private readonly ConcurrentDictionary<string, SceneEntity> _scenes = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IEnumerable<string> RegisteredScenes => _scenes.Keys;

    /// <inheritdoc/>
    public void RegisterScene(string sceneName, SceneEntity scene)
    {
        _scenes[sceneName] = scene;
    }

    /// <summary>
    /// 便捷重载——用 params UIElementEntity 直接注册场景
    /// </summary>
    public void Register(string sceneName, params UIElementEntity[] elements)
    {
        _scenes[sceneName] = new SceneEntity
        {
            SceneName = sceneName,
            Elements = elements.ToList()
        };
    }

    /// <inheritdoc/>
    public SceneEntity? FindScene(string sceneName)
    {
        return _scenes.TryGetValue(sceneName, out var scene) ? scene : null;
    }

    /// <inheritdoc/>
    public bool HasScene(string sceneName) => _scenes.ContainsKey(sceneName);
}
