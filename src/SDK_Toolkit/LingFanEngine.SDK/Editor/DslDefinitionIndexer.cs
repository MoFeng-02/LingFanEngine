using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LingFanEngine.SDK.Editor;

/// <summary>
/// DSL 定义索引器——扫描项目 Stories/ 目录，索引所有可导航的定义。
/// <para>P0-2: 从仅索引 scene/label/character 扩展到索引
/// style/variable/array/dict/func/sprite 等所有可导航定义。</para>
/// </summary>
public class DslDefinitionIndexer
{
    // 正则表达式用于快速提取定义（性能优于全文解析）
    private static readonly Regex s_sceneRegex = new(@"^\s*scene\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex s_labelRegex = new(@"^\s*label\s+(\w+)\s*:", RegexOptions.Compiled);
    private static readonly Regex s_characterRegex = new(@"^\s*character\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex s_styleRegex = new(@"^\s*style\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex s_variableRegex = new(@"^\s*(?:set|define|let|local)\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex s_arrayRegex = new(@"^\s*array\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex s_dictRegex = new(@"^\s*dict\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex s_funcRegex = new(@"^\s*func\s+(\w+)\s*\(", RegexOptions.Compiled);
    private static readonly Regex s_spriteRegex = new(@"^\s*sprite\s+""([^""]+)""", RegexOptions.Compiled);

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

    /// <summary>样式名集合（P0-2）</summary>
    public List<string> StyleNames => Definitions
        .Where(d => d.Type == DefinitionType.Style)
        .Select(d => d.Name)
        .Distinct()
        .ToList();

    /// <summary>变量名集合（P0-2）</summary>
    public List<string> VariableNames => Definitions
        .Where(d => d.Type == DefinitionType.Variable)
        .Select(d => d.Name)
        .Distinct()
        .ToList();

    /// <summary>函数名集合（P0-2）</summary>
    public List<string> FunctionNames => Definitions
        .Where(d => d.Type == DefinitionType.Function)
        .Select(d => d.Name)
        .Distinct()
        .ToList();

    /// <summary>Sprite ID 集合（P0-2）</summary>
    public List<string> SpriteIds => Definitions
        .Where(d => d.Type == DefinitionType.Sprite)
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
            await IndexFileInternalAsync(file);
        }
    }

    /// <summary>按名称查找定义</summary>
    public DslDefinition? FindDefinition(string name, DefinitionType? type = null)
    {
        return Definitions.FirstOrDefault(d =>
            d.Name == name && (type == null || d.Type == type));
    }

    /// <summary>按名称查找所有同名定义（用于重复定义检测）</summary>
    public List<DslDefinition> FindAllDefinitions(string name, DefinitionType? type = null)
    {
        return Definitions
            .Where(d => d.Name == name && (type == null || d.Type == type))
            .ToList();
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
        await IndexFileInternalAsync(filePath);
    }

    private async Task IndexFileInternalAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                TryMatch(s_sceneRegex, line, filePath, i + 1, DefinitionType.Scene);
                TryMatch(s_labelRegex, line, filePath, i + 1, DefinitionType.Label);
                TryMatch(s_characterRegex, line, filePath, i + 1, DefinitionType.Character);
                TryMatch(s_styleRegex, line, filePath, i + 1, DefinitionType.Style);
                TryMatch(s_variableRegex, line, filePath, i + 1, DefinitionType.Variable);
                TryMatch(s_arrayRegex, line, filePath, i + 1, DefinitionType.Array);
                TryMatch(s_dictRegex, line, filePath, i + 1, DefinitionType.Dict);
                TryMatch(s_funcRegex, line, filePath, i + 1, DefinitionType.Function);
                TryMatch(s_spriteRegex, line, filePath, i + 1, DefinitionType.Sprite);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DslDefinitionIndexer] 文件读取失败: {ex.Message}");
        }
    }

    private void TryMatch(Regex regex, string line, string filePath, int lineNum, DefinitionType type)
    {
        var match = regex.Match(line);
        if (match.Success)
        {
            Definitions.Add(new DslDefinition(
                match.Groups[1].Value,
                type,
                filePath,
                lineNum));
        }
    }
}

/// <summary>DSL 定义类型</summary>
public enum DefinitionType
{
    Scene,
    Label,
    Character,
    // P0-2 新增
    Style,
    Variable,
    Array,
    Dict,
    Function,
    Sprite,
}

/// <summary>DSL 定义项</summary>
public record DslDefinition(string Name, DefinitionType Type, string FilePath, int Line);
