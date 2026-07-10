using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 定义索引器——扫描项目 Stories/ 目录，索引场景名、标签、角色键。
/// <para>用于 Go to Definition 和代码补全。</para>
/// </summary>
public class DslDefinitionIndexer
{
    // 正则表达式用于快速提取定义（性能优于全文解析）
    private static readonly Regex s_sceneRegex = new(@"^\s*scene\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex s_labelRegex = new(@"^\s*label\s+(\w+)\s*:", RegexOptions.Compiled);
    private static readonly Regex s_characterRegex = new(@"^\s*character\s+""([^""]+)""", RegexOptions.Compiled);

    /// <summary>所有索引的定义</summary>
    public List<DslDefinition> Definitions { get; private set; } = [];

    /// <summary>场景名集合</summary>
    public List<string> SceneNames => Definitions
        .Where(d => d.Type == DefinitionType.Scene)
        .Select(d => d.Name)
        .Distinct()
        .ToList();

    /// <summary>标签名集合</summary>
    public List<string> LabelNames => Definitions
        .Where(d => d.Type == DefinitionType.Label)
        .Select(d => d.Name)
        .Distinct()
        .ToList();

    /// <summary>角色键集合</summary>
    public List<string> CharacterKeys => Definitions
        .Where(d => d.Type == DefinitionType.Character)
        .Select(d => d.Name)
        .Distinct()
        .ToList();

    /// <summary>扫描 Stories 目录并重建索引</summary>
    public async Task ReindexAsync(string storiesDirectory)
    {
        Definitions.Clear();

        if (!Directory.Exists(storiesDirectory))
            return;

        var storyFiles = Directory.GetFiles(storiesDirectory, "*.story", SearchOption.AllDirectories);
        foreach (var file in storyFiles)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];

                    // 场景定义
                    var sceneMatch = s_sceneRegex.Match(line);
                    if (sceneMatch.Success)
                    {
                        Definitions.Add(new DslDefinition(
                            sceneMatch.Groups[1].Value,
                            DefinitionType.Scene,
                            file,
                            i + 1));
                    }

                    // 标签定义
                    var labelMatch = s_labelRegex.Match(line);
                    if (labelMatch.Success)
                    {
                        Definitions.Add(new DslDefinition(
                            labelMatch.Groups[1].Value,
                            DefinitionType.Label,
                            file,
                            i + 1));
                    }

                    // 角色定义
                    var charMatch = s_characterRegex.Match(line);
                    if (charMatch.Success)
                    {
                        Definitions.Add(new DslDefinition(
                            charMatch.Groups[1].Value,
                            DefinitionType.Character,
                            file,
                            i + 1));
                    }
                }
            }
            catch
            {
                // 文件读取失败——跳过
            }
        }
    }

    /// <summary>按名称查找定义</summary>
    public DslDefinition? FindDefinition(string name, DefinitionType? type = null)
    {
        return Definitions.FirstOrDefault(d =>
            d.Name == name && (type == null || d.Type == type));
    }

    /// <summary>按名称前缀查找定义</summary>
    public List<DslDefinition> FindDefinitionsByPrefix(string prefix, DefinitionType? type = null)
    {
        return Definitions
            .Where(d =>
                d.Name.StartsWith(prefix) && (type == null || d.Type == type))
            .ToList();
    }

    /// <summary>增量索引单个文件</summary>
    public async Task IndexFileAsync(string filePath)
    {
        // 移除旧索引
        Definitions.RemoveAll(d => d.FilePath == filePath);

        if (!File.Exists(filePath))
            return;

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var sceneMatch = s_sceneRegex.Match(line);
                if (sceneMatch.Success)
                {
                    Definitions.Add(new DslDefinition(
                        sceneMatch.Groups[1].Value,
                        DefinitionType.Scene, filePath, i + 1));
                }

                var labelMatch = s_labelRegex.Match(line);
                if (labelMatch.Success)
                {
                    Definitions.Add(new DslDefinition(
                        labelMatch.Groups[1].Value,
                        DefinitionType.Label, filePath, i + 1));
                }

                var charMatch = s_characterRegex.Match(line);
                if (charMatch.Success)
                {
                    Definitions.Add(new DslDefinition(
                        charMatch.Groups[1].Value,
                        DefinitionType.Character, filePath, i + 1));
                }
            }
        }
        catch
        {
            // 忽略错误
        }
    }
}

/// <summary>DSL 定义类型</summary>
public enum DefinitionType
{
    Scene,
    Label,
    Character,
}

/// <summary>DSL 定义项</summary>
public record DslDefinition(string Name, DefinitionType Type, string FilePath, int Line);
