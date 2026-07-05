using System.Text.Json;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// 故事懒加载注册表
/// <para>启动时扫描 Stories 目录建立 "场景名 → 文件路径" 映射。</para>
/// <para>实际文件在第一次 navigate/scene 指令时才读取编译。</para>
/// </summary>
public class StoryRegistry
{
    private readonly ISceneRegistry _sceneRegistry;
    private readonly IScriptEngine _dslEngine;
    private readonly StoryLoader _storyLoader;
    private readonly string _storyRoot;
    private readonly Dictionary<string, string> _sceneToFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _labelToFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadedScenes = new(StringComparer.OrdinalIgnoreCase);
    // 编译后的命令和标签索引，供 DslExecutor 使用
    private readonly Dictionary<string, IReadOnlyList<ICommand>> _compiledCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _compiledLabels = new(StringComparer.OrdinalIgnoreCase);
    private bool _scanned;

    /// <summary>
    /// 已注册的场景数
    /// </summary>
    public int RegisteredCount => _sceneToFile.Count;

    /// <summary>
    /// 已加载的场景数
    /// </summary>
    public int LoadedCount => _loadedScenes.Count;

    public StoryRegistry(ISceneRegistry sceneRegistry, IScriptEngine dslEngine,
        StoryLoader storyLoader, string storyRoot = "Stories")
    {
        _sceneRegistry = sceneRegistry;
        _dslEngine = dslEngine;
        _storyLoader = storyLoader;
        _storyRoot = storyRoot;
    }

    /// <summary>
    /// 扫描目录，建立 场景名→文件路径 映射（不加载内容）
    /// </summary>
    public void Scan()
    {
        if (_scanned) return;
        _scanned = true;

        if (!Directory.Exists(_storyRoot)) return;

        var storyFiles = Directory.GetFiles(_storyRoot, "*.story", SearchOption.AllDirectories);
        foreach (var filePath in storyFiles)
        {
            // 扫描 scene "xxx" 定义（轻量，只提取场景名）
            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('#')) continue;

                // 匹配 scene "name"（顶层定义 scene "xxx"）
                var scMatch = System.Text.RegularExpressions.Regex.Match(trimmed,
                    @"^scene\s+""([^""]+)""$");
                if (scMatch.Success)
                {
                    var sceneName = scMatch.Groups[1].Value;
                    if (!_sceneToFile.ContainsKey(sceneName))
                        _sceneToFile[sceneName] = filePath;
                }

                // 匹配 label xxx:（全局 label 索引）
                var labelMatch = System.Text.RegularExpressions.Regex.Match(trimmed,
                    @"^label\s+(\w[\w\d_]*)\s*:$");
                if (labelMatch.Success)
                {
                    var labelName = labelMatch.Groups[1].Value;
                    if (!_labelToFile.ContainsKey(labelName))
                        _labelToFile[labelName] = filePath;
                }
            }
        }
    }

    /// <summary>
    /// 按需加载场景——查找文件、读取、编译、注册 SceneEntity
    /// </summary>
    public bool LoadScene(string sceneName)
    {
        if (_loadedScenes.Contains(sceneName))
            return true; // 已加载

        if (!_sceneToFile.TryGetValue(sceneName, out var filePath))
        {
            // 没找到映射，尝试直接按文件名搜索
            filePath = FindByFileName(sceneName);
            if (filePath == null) return false;
        }

        try
        {
            var content = File.ReadAllText(filePath);

            // 注册 define（StoryLoader 的 RegisterDefinesFromJson 直接写状态容器）
            _storyLoader.RegisterDefinesFromJson(new StoryFile
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                Title = Path.GetFileNameWithoutExtension(filePath),
                Script = content,
                DefinesNode = null
            }, content);

            // 提取 scene 块并注册到 SceneRegistry
            var (sceneBlocks, flowScript) = ExtractSceneBlocksWithFlow(content);
            foreach (var (name, elements, entryScript) in sceneBlocks)
            {
                if (_loadedScenes.Contains(name)) continue;

                // 编译 entry script
                List<ICommand>? entryCmds = null;
                if (!string.IsNullOrWhiteSpace(entryScript))
                {
                    var entryResult = _dslEngine.Compile(entryScript);
                    if (entryResult.Success && entryResult.Commands != null && entryResult.Commands.Count > 0)
                        entryCmds = entryResult.Commands.ToList();
                }

                _sceneRegistry.RegisterScene(name, new SceneEntity
                {
                    SceneName = name,
                    Elements = elements,
                    IsTransient = false,
                    EntryCommands = entryCmds
                });
                _loadedScenes.Add(name);
                System.Diagnostics.Debug.WriteLine($"[StoryRegistry] 注册场景: {name}, 元素数={elements.Count}, entryCmds={(entryCmds?.Count ?? 0)}");
            }

            // 编译剩余流程脚本
            var result = _dslEngine.Compile(flowScript);
            if (!result.Success)
            {
                System.Diagnostics.Debug.WriteLine($"[StoryRegistry] 编译失败: {filePath} -> {result.Error}");
                return false;
            }

            // 保存编译结果（供 DslExecutor 使用）
            if (result.Commands != null && result.Commands.Count > 0)
            {
                // 使用文件路径作为 key（同一个文件可能有多个 scene，共享相同的流程命令）
                _compiledCommands[filePath] = result.Commands;
                if (result.Labels != null)
                    _compiledLabels[filePath] = result.Labels;
            }

            // 已加载的场景标记
            if (!_loadedScenes.Contains(sceneName))
                _loadedScenes.Add(sceneName);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StoryRegistry] 加载失败: {filePath} -> {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取编译结果（命令列表 + 标签索引），供 DslExecutor 使用
    /// </summary>
    /// <summary>
    /// 按文件路径获取编译结果
    /// </summary>
    public (IReadOnlyList<ICommand>? Commands, IReadOnlyDictionary<string, int>? Labels)
        GetCompiledResultByFile(string filePath)
    {
        _compiledCommands.TryGetValue(filePath, out var cmds);
        _compiledLabels.TryGetValue(filePath, out var lbls);
        return (cmds, lbls);
    }

    public (IReadOnlyList<ICommand>? Commands, IReadOnlyDictionary<string, int>? Labels)
        GetCompiledResult(string sceneName)
    {
        if (_sceneToFile.TryGetValue(sceneName, out var filePath))
        {
            _compiledCommands.TryGetValue(filePath, out var cmds);
            _compiledLabels.TryGetValue(filePath, out var lbls);
            return (cmds, lbls);
        }
        return (null, null);
    }

    /// <summary>
    /// 获取所有已知的 story 文件路径（去重）
    /// </summary>
    public IEnumerable<string> GetAllStoryFiles()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in _sceneToFile.Values)
        {
            if (visited.Add(filePath))
                yield return filePath;
        }
    }

    /// <summary>
    /// 按文件路径加载场景和流程命令（不依赖 scene 名）
    /// <para>返回 true 表示编译成功（无论是否有 scene 块）。</para>
    /// </summary>
    public bool LoadSceneFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        try
        {
            var content = File.ReadAllText(filePath);
            _storyLoader.RegisterDefinesFromJson(new StoryFile
            {
                Id = Path.GetFileNameWithoutExtension(filePath),
                Title = Path.GetFileNameWithoutExtension(filePath),
                Script = content,
                DefinesNode = null
            }, content);

            var (sceneBlocks, flowScript) = ExtractSceneBlocksWithFlow(content);
            foreach (var (name, elements, entryScript) in sceneBlocks)
            {
                if (_loadedScenes.Contains(name)) continue;

                List<ICommand>? entryCmds = null;
                if (!string.IsNullOrWhiteSpace(entryScript))
                {
                    var entryResult = _dslEngine.Compile(entryScript);
                    if (entryResult.Success && entryResult.Commands != null && entryResult.Commands.Count > 0)
                        entryCmds = entryResult.Commands.ToList();
                }

                _sceneRegistry.RegisterScene(name, new SceneEntity
                {
                    SceneName = name,
                    Elements = elements,
                    IsTransient = false,
                    EntryCommands = entryCmds
                });
                _loadedScenes.Add(name);
            }

            var result = _dslEngine.Compile(flowScript);
            if (!result.Success) return false;
            if (result.Commands != null && result.Commands.Count > 0)
            {
                _compiledCommands[filePath] = result.Commands;
                if (result.Labels != null)
                    _compiledLabels[filePath] = result.Labels;
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 通过 label 名查找对应的文件路径
    /// </summary>
    public string? FindFileByLabel(string label)
    {
        return _labelToFile.TryGetValue(label, out var path) ? path : null;
    }

    /// <summary>
    /// 确保 label 所在的文件已被加载并编译
    /// <para>返回 true 表示文件已就绪（编译结果在 GetCompiledResultByFile 中可查）。</para>
    /// </summary>
    public bool EnsureLabelLoaded(string label)
    {
        if (!_labelToFile.TryGetValue(label, out var filePath))
            return false;
        if (_compiledCommands.ContainsKey(filePath))
            return true; // 已编译
        return LoadSceneFromFile(filePath);
    }

    /// <summary>
    /// 扫描完成后，注册所有已知文件的 define 到状态容器
    /// </summary>
    public void RegisterAllDefines()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sceneName, filePath) in _sceneToFile)
        {
            if (!visited.Add(filePath)) continue; // 每个文件只处理一次
            try
            {
                var content = File.ReadAllText(filePath);
                _storyLoader.RegisterDefinesFromJson(new StoryFile
                {
                    Id = Path.GetFileNameWithoutExtension(filePath),
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    Script = content,
                    DefinesNode = null
                }, content);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[StoryRegistry] Parse error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// 检查场景是否可加载（已在映射表中或文件存在）
    /// </summary>
    public bool CanLoad(string sceneName)
    {
        if (_loadedScenes.Contains(sceneName) || _sceneToFile.ContainsKey(sceneName))
            return true;
        return FindByFileName(sceneName) != null;
    }

    /// <summary>
    /// 从 DSL 脚本中提取 scene 块并返回剩余流程脚本
    /// </summary>
    private static (List<(string SceneName, List<UIElementEntity> Elements, string EntryScript)> Scenes, string FlowScript)
        ExtractSceneBlocksWithFlow(string content)
    {
        return StoryLoader.ExtractSceneBlocks(content);
    }

    private string? FindByFileName(string sceneName)
    {
        // 尝试 Stories/**/{sceneName}.story 或 Stories/**/{sceneName}_*.story
        var pattern = $"{sceneName}*.story";
        var files = Directory.GetFiles(_storyRoot, pattern, SearchOption.AllDirectories);
        if (files.Length > 0) return files[0];

        // 尝试 Stories/**/*{sceneName}*.story
        pattern = $"*{sceneName}*.story";
        files = Directory.GetFiles(_storyRoot, pattern, SearchOption.AllDirectories);
        if (files.Length > 0) return files[0];

        return null;
    }
}
