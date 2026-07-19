using System.Collections.Concurrent;
using System.Text.Json;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.DslCore;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// 故事懒加载注册表
/// <para>启动时扫描 Stories 目录建立 "场景名 → 文件路径" 映射。</para>
/// <para>实际文件在第一次 navigate/scene 指令时才读取编译。</para>
/// </summary>
public class StoryRegistry : IStoryRegistry
{
    private readonly ISceneRegistry _sceneRegistry;
    private readonly IScriptEngine _dslEngine;
    private readonly StoryLoader _storyLoader;
    private readonly string _storyRoot;
    private readonly IEncryptedFileReader? _fileReader;
    private readonly ConcurrentDictionary<string, string> _sceneToFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _labelToFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _loadedScenes = new(StringComparer.OrdinalIgnoreCase);
    // 编译后的命令和标签索引，供 DslExecutor 使用
    private readonly ConcurrentDictionary<string, IReadOnlyList<ICommand>> _compiledCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, int>> _compiledLabels = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _scanned;

    /// <summary>
    /// 已注册的场景数
    /// </summary>
    public int RegisteredCount => _sceneToFile.Count;

    /// <summary>
    /// 已加载的场景数
    /// </summary>
    public int LoadedCount => _loadedScenes.Count;

    public StoryRegistry(ISceneRegistry sceneRegistry, IScriptEngine dslEngine,
        StoryLoader storyLoader, string storyRoot = "Stories",
        IEncryptedFileReader? fileReader = null)
    {
        _sceneRegistry = sceneRegistry;
        _dslEngine = dslEngine;
        _storyLoader = storyLoader;
        _storyRoot = storyRoot;
        _fileReader = fileReader;
    }

    /// <summary>
    /// 读取文件全部文本（自动检测并解密 LFEN 格式）。
    /// <para>Phase 56 修复：StoryRegistry 必须通过 IEncryptedFileReader 读取，
    /// 否则加密后的 .story 文件读到乱码，无法解析场景定义。</para>
    /// </summary>
    private string? ReadAllText(string filePath)
    {
        if (_fileReader != null)
        {
            using var stream = _fileReader.OpenRead(filePath);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return File.ReadAllText(filePath);
    }

    /// <summary>
    /// 扫描目录，建立 场景名→文件路径 映射（不加载内容）
    /// </summary>
    public void Scan()
    {
        if (_scanned) return;
        _scanned = true;

        var resolvedRoot = ResolveStoryRoot();
        if (resolvedRoot == null) return;

        var storyFiles = Directory.GetFiles(resolvedRoot, "*.story", SearchOption.AllDirectories);
        foreach (var filePath in storyFiles)
        {
            // Phase 57: try-catch 防止单个文件解密/解析异常导致整个 Scan 崩溃
            try
            {
                // 扫描 scene "xxx" 定义（轻量，只提取场景名）
                // Phase 56：通过 IEncryptedFileReader 读取（加密文件自动解密）
                var content = ReadAllText(filePath);
                if (content == null) continue;
                using var stringReader = new StringReader(content);
                string? line;
                while ((line = stringReader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith('#')) continue;

                    // 计算原始行缩进（Tab 按 4 空格计算）——与 ExtractSceneBlocks 一致
                    // 仅顶格行（indent=0）的 scene/label 视为定义；
                    // 缩进的 scene 是 DSL 导航命令，缩进的 label 不存在（但防御性跳过）
                    var lineIndent = 0;
                    foreach (var ch in line)
                    {
                        if (ch == ' ') lineIndent++;
                        else if (ch == '\t') lineIndent += 4;
                        else break;
                    }
                    if (lineIndent != 0) continue;

                    // 匹配 scene "name"（支持 scene 行属性如 layout=canvas type=menu）
                    // 快速前缀过滤 + Pidgin 解析器
                    if (trimmed.StartsWith("scene ") || trimmed.StartsWith("scene\t"))
                    {
                        var sceneHeader = DslParser.ParseSceneHeader(trimmed);
                        if (sceneHeader != null)
                        {
                            var sceneName = sceneHeader.SceneName;
                            if (!_sceneToFile.ContainsKey(sceneName))
                                _sceneToFile[sceneName] = filePath;
                        }
                    }

                    // 匹配 label xxx:（全局 label 索引）
                    // 快速前缀过滤 + Pidgin 解析器
                    if (trimmed.StartsWith("label ") || trimmed.StartsWith("label\t"))
                    {
                        var labelName = DslParser.ParseLabelLine(trimmed);
                        if (labelName != null)
                        {
                            if (!_labelToFile.ContainsKey(labelName))
                                _labelToFile[labelName] = filePath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[StoryRegistry] Scan 文件失败: {filePath} -> {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 按需加载场景——查找文件、读取、编译、注册 SceneEntity
    /// </summary>
    public bool LoadScene(string sceneName)
    {
        if (_loadedScenes.ContainsKey(sceneName))
            return true; // 已加载

        if (!_sceneToFile.TryGetValue(sceneName, out var filePath))
        {
            // 没找到映射，尝试直接按文件名搜索
            filePath = FindByFileName(sceneName);
            if (filePath == null) return false;
        }

        try
        {
            var content = ReadAllText(filePath);
            if (content == null) return false;
            if (!LoadSceneInternal(filePath, content))
            {
                System.Diagnostics.Debug.WriteLine($"[StoryRegistry] 编译失败: {filePath}");
                return false;
            }

            // 已加载的场景标记
            if (!_loadedScenes.ContainsKey(sceneName))
                _loadedScenes.TryAdd(sceneName, 0);
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
            var content = ReadAllText(filePath);
            if (content == null) return false;
            return LoadSceneInternal(filePath, content);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[StoryRegistry] LoadSceneFromFile failed: {filePath} — {ex.Message}"); return false; }
    }

    /// <summary>
    /// 内部加载逻辑——读文件、提取场景块、注入 defines、编译流程脚本、注册场景。
    /// <para>LoadScene 和 LoadSceneFromFile 的公共实现，消除代码重复。</para>
    /// <para>步骤：RegisterDefinesFromJson → ExtractSceneBlocks → InjectGlobalDefines →
    /// 注册场景+追加 entryScript 为 label → 编译 flowScript → 保存编译结果。</para>
    /// </summary>
    /// <param name="filePath">文件路径（作为编译结果的 key）</param>
    /// <param name="content">文件内容</param>
    /// <returns>true 表示编译成功</returns>
    private bool LoadSceneInternal(string filePath, string content)
    {
        // JSON 格式 defines（defines 字段）
        _storyLoader.RegisterDefinesFromJson(new StoryFile
        {
            Id = Path.GetFileNameWithoutExtension(filePath),
            Title = Path.GetFileNameWithoutExtension(filePath),
            Script = content,
            DefinesNode = null
        }, content);

        // 提取 scene 块 + 全局 defines
        var (sceneBlocks, flowScript, globalDefines) = ExtractSceneBlocksWithFlow(content);

        // 注入全局 defines（顶格 define，加载时即生效）
        _storyLoader.InjectGlobalDefines(globalDefines);

        // 注册场景 + 将 entryScript 追加为 label
        var flowBuilder = new System.Text.StringBuilder(flowScript);
        foreach (var (name, elements, entryScript, defines, layoutMode, sceneType) in sceneBlocks)
        {
            if (_loadedScenes.ContainsKey(name)) continue;

            // 将 entryScript 追加到 flowScript 中作为 label <sceneName>:
            // 这样 NavigateHandler 可以通过场景同名 label 启动 DslExecutor
            // （与 StoryLoader.LoadFromFileAsync 的行为一致）
            if (!string.IsNullOrWhiteSpace(entryScript))
            {
                flowBuilder.Append($"\nlabel {name}:\n");
                flowBuilder.Append(entryScript);
                flowBuilder.Append('\n');
            }

            _sceneRegistry.RegisterScene(name, new SceneEntity
            {
                SceneName = name,
                Elements = elements,
                IsTransient = false,
                Defines = defines,
                LayoutMode = layoutMode,
                SceneType = sceneType
            });
            _loadedScenes.TryAdd(name, 0);
            System.Diagnostics.Debug.WriteLine($"[StoryRegistry] 注册场景: {name}, 元素数={elements.Count}, entry={(string.IsNullOrWhiteSpace(entryScript) ? 0 : entryScript.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length)} 行, defines={defines?.Count ?? 0}");
        }

        // 编译剩余流程脚本（含场景 entryScript 转为的 label）
        var result = _dslEngine.Compile(flowBuilder.ToString());
        if (!result.Success)
        {
            System.Diagnostics.Debug.WriteLine($"[StoryRegistry] 编译失败: {filePath} -> {result.Error}");
            return false;
        }

        // 保存编译结果（使用文件路径作为 key——同一个文件可能有多个 scene，共享相同的流程命令）
        if (result.Commands != null && result.Commands.Count > 0)
        {
            _compiledCommands[filePath] = result.Commands;
            if (result.Labels != null)
                _compiledLabels[filePath] = result.Labels;
        }

        return true;
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
    /// <para>支持两种格式：JSON 格式（defines 字段）和 DSL 格式（顶格 define 语句）。</para>
    /// <para>JSON defines 通过 RegisterDefinesFromJson 注入；DSL 全局 defines 通过 ExtractSceneBlocks + InjectGlobalDefines 注入。</para>
    /// <para>场景级 defines 不在此处注入——它们在导航到对应场景时通过 MergeIntoState 深合并注入。</para>
    /// </summary>
    public void RegisterAllDefines()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sceneName, filePath) in _sceneToFile)
        {
            if (!visited.Add(filePath)) continue; // 每个文件只处理一次
            try
            {
                var content = ReadAllText(filePath);
                if (content == null) continue;

                // JSON 格式 defines（defines 字段）
                _storyLoader.RegisterDefinesFromJson(new StoryFile
                {
                    Id = Path.GetFileNameWithoutExtension(filePath),
                    Title = Path.GetFileNameWithoutExtension(filePath),
                    Script = content,
                    DefinesNode = null
                }, content);

                // DSL 格式全局 defines（顶格 define 语句，Phase 39 修复）
                var (_, _, globalDefines) = ExtractSceneBlocksWithFlow(content);
                _storyLoader.InjectGlobalDefines(globalDefines);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[StoryRegistry] Parse error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// 检查场景是否可加载（已在映射表中或文件存在）
    /// </summary>
    public bool CanLoad(string sceneName)
    {
        if (_loadedScenes.ContainsKey(sceneName) || _sceneToFile.ContainsKey(sceneName))
            return true;
        return FindByFileName(sceneName) != null;
    }

    /// <summary>
    /// 热重载：重新加载指定文件的所有场景和编译结果
    /// <para>清除该文件的已加载标记和编译缓存，重新读取、编译、注册。</para>
    /// <para>返回该文件包含的所有场景名列表（用于 UI 刷新判断）。</para>
    /// </summary>
    public List<string> ReloadFile(string filePath)
    {
        var affectedScenes = new List<string>();

        // 1. 找出该文件对应的所有场景名
        foreach (var (sceneName, file) in _sceneToFile)
        {
            if (string.Equals(file, filePath, StringComparison.OrdinalIgnoreCase))
                affectedScenes.Add(sceneName);
        }

        // 2. 从已加载集合中移除这些场景
        foreach (var sceneName in affectedScenes)
            _loadedScenes.TryRemove(sceneName, out _);

        // 3. 清除编译缓存
        _compiledCommands.TryRemove(filePath, out _);
        _compiledLabels.TryRemove(filePath, out _);

        // 4. 重新扫描文件更新 scene/label 映射
        RescanFile(filePath);

        // 5. 重新加载文件
        LoadSceneFromFile(filePath);

        System.Diagnostics.Debug.WriteLine(
            $"[StoryRegistry] 热重载: {filePath}, 影响场景: {string.Join(", ", affectedScenes)}");

        return affectedScenes;
    }

    /// <summary>
    /// 重新扫描单个文件的 scene/label 定义，更新映射表
    /// </summary>
    private void RescanFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        // 移除该文件的旧映射
        var keysToRemove = _sceneToFile.Where(kv => 
            string.Equals(kv.Value, filePath, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
        foreach (var key in keysToRemove)
            _sceneToFile.TryRemove(key, out _);

        var labelKeysToRemove = _labelToFile.Where(kv => 
            string.Equals(kv.Value, filePath, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
        foreach (var key in labelKeysToRemove)
            _labelToFile.TryRemove(key, out _);

        // 重新扫描
        try
        {
            var content = ReadAllText(filePath);
            if (content == null) return;
            using var stringReader = new StringReader(content);
            string? line;
            while ((line = stringReader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith('#')) continue;

                var lineIndent = 0;
                foreach (var ch in line)
                {
                    if (ch == ' ') lineIndent++;
                    else if (ch == '\t') lineIndent += 4;
                    else break;
                }
                if (lineIndent != 0) continue;

                if (trimmed.StartsWith("scene ") || trimmed.StartsWith("scene\t"))
                {
                    var sceneHeader = DslParser.ParseSceneHeader(trimmed);
                    if (sceneHeader != null)
                        _sceneToFile[sceneHeader.SceneName] = filePath;
                }

                if (trimmed.StartsWith("label ") || trimmed.StartsWith("label\t"))
                {
                    var labelName = DslParser.ParseLabelLine(trimmed);
                    if (labelName != null)
                        _labelToFile[labelName] = filePath;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StoryRegistry] RescanFile failed: {filePath} -> {ex.Message}");
        }
    }

    /// <summary>
    /// 从 DSL 脚本中提取 scene 块并返回剩余流程脚本
    /// </summary>
    private (List<(string SceneName, List<UIElementEntity> Elements, string EntryScript, Dictionary<string, object?>? Defines, string LayoutMode, Abstractions.Entities.Enums.SceneType SceneType)> Scenes, string FlowScript, Dictionary<string, object?>? GlobalDefines)
        ExtractSceneBlocksWithFlow(string content)
    {
        return _storyLoader.ExtractSceneBlocks(content);
    }

    private string? FindByFileName(string sceneName)
    {
        var resolvedRoot = ResolveStoryRoot();
        if (resolvedRoot == null) return null;

        // 尝试 Stories/**/{sceneName}.story 或 Stories/**/{sceneName}_*.story
        var pattern = $"{sceneName}*.story";
        var files = Directory.GetFiles(resolvedRoot, pattern, SearchOption.AllDirectories);
        if (files.Length > 0) return files[0];

        // 尝试 Stories/**/*{sceneName}*.story
        pattern = $"*{sceneName}*.story";
        files = Directory.GetFiles(resolvedRoot, pattern, SearchOption.AllDirectories);
        if (files.Length > 0) return files[0];

        return null;
    }

    /// <summary>
    /// 解析故事根目录路径
    /// <para>搜索顺序：传入路径 → AppContext.BaseDirectory 下的同名子目录 → 项目根目录</para>
    /// </summary>
    private string? ResolveStoryRoot()
    {
        // 1. 直接存在
        if (Directory.Exists(_storyRoot))
            return _storyRoot;

        // 2. 输出目录下
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var outputDir = Path.Combine(baseDir, _storyRoot);
            if (Directory.Exists(outputDir))
                return outputDir;
        }

        // 3. 当前工作目录向上搜索
        var current = Path.GetFullPath(".");
        for (int i = 0; i < 5; i++)
        {
            var probe = Path.Combine(current, _storyRoot);
            if (Directory.Exists(probe))
                return probe;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return null;
    }
}
