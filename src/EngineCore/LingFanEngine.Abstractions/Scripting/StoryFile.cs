using System.Text.Json.Nodes;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.Scripting;

/// <summary>
/// 故事文件元数据
/// <para>已迁移至 Abstractions 层，便于 IStoryLoader 接口引用。</para>
/// </summary>
public class StoryFile
{
    /// <summary>故事唯一标识</summary>
    public required string Id { get; set; }

    /// <summary>故事标题</summary>
    public required string Title { get; set; }

    /// <summary>作者（可选）</summary>
    public string? Author { get; set; }

    /// <summary>语言代码，如 zh-CN, en-US（可选，用于多语言故事）</summary>
    public string? Lang { get; set; }

    /// <summary>故事脚本内容</summary>
    public required string Script { get; set; }

    /// <summary>故事文件所在子目录（如 "chapter1_初端"，用于场景分组）</summary>
    public string? Directory { get; set; }

    /// <summary>关联场景名称（可选）</summary>
    public string? SceneName { get; set; }

    /// <summary>编译后的命令列表（JSON 序列化时忽略）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<ICommand>? CompiledCommands { get; set; }

    /// <summary>标签位置映射（标签名 → 命令索引，JSON 序列化时忽略）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, int>? Labels { get; set; }

    /// <summary>JSON 格式的 defines 字段原始节点（JSON story 文件专用）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public JsonNode? DefinesNode { get; set; }
}
