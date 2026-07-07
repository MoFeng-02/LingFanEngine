using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Entities.Medias;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;

namespace LingFanEngine.Abstractions.Entities.Scenes;

/// <summary>
/// 场景实体
/// <para>场景是导航的最小目标单位，包含 UI 元素列表。</para>
/// </summary>
public class SceneEntity : BaseEntity
{
    /// <summary>
    /// 场景名称，用于路由引用和存档标识
    /// </summary>
    public required string SceneName { get; set; }

    public SceneType SceneType { get; set; } = SceneType.Game;

    /// <summary>
    /// 核心：场景包含的 UI 元素列表——这就是 List 驱动的体现
    /// </summary>
    public required List<UIElementEntity> Elements { get; set; }

    /// <summary>
    /// 场景布局模式：grid（默认）、canvas（绝对定位）、stack（流式）、panel（叠加层）
    /// <para>对应 Avalonia 的 Grid/Canvas/StackPanel/Panel 根容器。</para>
    /// </summary>
    public string LayoutMode { get; set; } = "grid";

    /// <summary>
    /// 场景入口脚本——进入场景时自动执行的命令列表
    /// <para>如 set/say/transition 等，在场景显示后自动按序执行。</para>
    /// </summary>
    public List<ICommand>? EntryCommands { get; set; }

    /// <summary>
    /// 场景级变量定义——进入场景时深合并到状态容器（补缺+修类型）
    /// <para>等价于 C# StoryScript 的 InDefines()，支持嵌套字典递归合并。</para>
    /// <para>DSL 语法：在 scene 块内使用 define "key" value once</para>
    /// </summary>
    public Dictionary<string, object?>? Defines { get; set; }

    /// <summary>
    /// 场景背景（图片/视频）
    /// </summary>
    public MediaEntity? Background { get; set; }

    /// <summary>
    /// 场景背景音乐
    /// </summary>
    public MediaEntity? Bgm { get; set; }

    /// <summary>
    /// 是否单例（进入时复用已有实例），默认 false
    /// </summary>
    public bool IsSingleton { get; set; }

    /// <summary>
    /// 是否瞬态（每次进入重新构建），默认 false
    /// <para>瞬态场景适合临时状态场景（如小游戏）；单例适合主菜单、标题画面等。</para>
    /// </summary>
    public bool IsTransient { get; set; }
}
