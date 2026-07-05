using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.UIs;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 场景注册表接口
/// <para>管理所有已知场景的注册和查找，供渲染器按场景名获取 UIElement 列表。</para>
/// </summary>
public interface ISceneRegistry
{
    void RegisterScene(string sceneName, SceneEntity scene);
    void Register(string sceneName, params UIElementEntity[] elements);
    SceneEntity? FindScene(string sceneName);
    IEnumerable<string> RegisteredScenes { get; }
    bool HasScene(string sceneName);
}
