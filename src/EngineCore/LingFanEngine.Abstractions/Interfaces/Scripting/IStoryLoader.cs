using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Scripting;

namespace LingFanEngine.Abstractions.Interfaces.Scripting;

/// <summary>
/// 故事文件加载管线接口
/// <para>支持 JSON 和纯 DSL 两种格式的 .story 文件。</para>
/// </summary>
public interface IStoryLoader
{
    /// <summary>从 JSON defines 节点注册变量定义到状态容器</summary>
    void RegisterDefinesFromJson(StoryFile storyFile, string rawContent);

    /// <summary>从 DSL 脚本中提取 scene 块并返回剩余流程脚本（含场景级 Defines、LayoutMode、SceneType、全局 Defines）</summary>
    (List<(string SceneName, List<UIElementEntity> Elements, string EntryScript, Dictionary<string, object?>? Defines, string LayoutMode, SceneType SceneType)> Scenes, string FlowScript, Dictionary<string, object?>? GlobalDefines)
        ExtractSceneBlocks(string content);
}
