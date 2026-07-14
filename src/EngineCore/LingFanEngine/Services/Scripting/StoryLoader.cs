using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.DslCore;
using LingFanEngine.Services.Resources;

namespace LingFanEngine.Services.Scripting;

// StoryFile 已迁移至 Abstractions/Scripting/StoryFile.cs

/// <summary>
/// .story 文件加载管线
/// <para>支持 JSON 和纯 DSL 两种格式的 .story 文件。
/// 文件名约定：{id}_{lang}.story，如 chapter1_zh-CN.story。
/// 加载时根据当前语言自动选择对应文件。</para>
/// </summary>
public class StoryLoader : IStoryLoader
{
    private readonly IScriptEngine _dslEngine;
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    private readonly ISceneRegistry _sceneRegistry;
    private readonly IDslExecutor? _dslExecutor;
    private readonly PackLoader? _packLoader;
    private readonly IEncryptedFileReader? _fileReader;
    private readonly Dictionary<string, List<StoryFile>> _loadedStories = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLang = "zh-CN";

    /// <summary>
    /// 已加载的故事数量
    /// </summary>
    public int LoadedCount => _loadedStories.Values.Sum(list => list.Count);

    /// <summary>
    /// 构造函数
    /// </summary>
    public StoryLoader(IScriptEngine dslEngine, ICommandPipeline pipeline, IStateContainer state,
        ISceneRegistry sceneRegistry,
        IDslExecutor? dslExecutor = null, PackLoader? packLoader = null,
        IEncryptedFileReader? fileReader = null)
    {
        _dslEngine = dslEngine;
        _pipeline = pipeline;
        _state = state;
        _sceneRegistry = sceneRegistry;
        _dslExecutor = dslExecutor;
        _packLoader = packLoader;
        _fileReader = fileReader;
    }

    /// <summary>
    /// 设置当前语言并重新加载所有已加载的 .story 文件
    /// <para>切换语言后，引擎自动加载对应语言的故事版本。</para>
    /// </summary>
    /// <param name="lang">语言代码，如 zh-CN、en-US</param>
    public void SetLanguage(string lang)
    {
        if (_currentLang == lang) return;
        _currentLang = lang;
        _state.Set(StateKeys.Story.Lang, lang);
    }

    /// <summary>
    /// 获取当前语言代码
    /// </summary>
    public string CurrentLang => _currentLang;

    /// <summary>
    /// 根据故事 ID 和当前语言构建文件名
    /// <para>按优先级查找：{id}_{lang}.story > {id}.story > 目录中任意语言版本</para>
    /// </summary>
    public static string? ResolveStoryFile(string directory, string storyId, string lang)
    {
        // 先解析目录（支持输出目录和项目根目录回退）
        var resolvedDir = ResolveDirectory(directory);
        if (resolvedDir == null) return null;

        // 1. 精确语言匹配
        var langFile = Path.Combine(resolvedDir, $"{storyId}_{lang}.story");
        if (File.Exists(langFile)) return langFile;

        // 2. 无语言后缀
        var bareFile = Path.Combine(resolvedDir, $"{storyId}.story");
        if (File.Exists(bareFile)) return bareFile;

        // 3. 目录中任意语言版本
        var pattern = $"{storyId}_*.story";
        var files = Directory.GetFiles(resolvedDir, pattern, SearchOption.TopDirectoryOnly);
        if (files.Length > 0) return files[0];

        return null;
    }

    /// <summary>
    /// 从文件加载单个 .story 文件
    /// <para>先尝试按 JSON 解析，失败则视为纯 DSL 脚本。</para>
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>加载的故事文件，失败返回 null</returns>
    public async ValueTask<StoryFile?> LoadFromFileAsync(string filePath, CancellationToken ct = default)
    {
        // 读取优先级：PackLoader → EncryptedFileReader（即解即用）→ 文件系统回退
        string content;
        var packPath = filePath.Replace('\\', '/');
        var packData = _packLoader != null ? await _packLoader.ReadBytesAsync(packPath, ct) : null;
        if (packData != null)
        {
            content = Encoding.UTF8.GetString(packData);
        }
        else
        {
            // 尝试原始路径
            var resolvedPath = File.Exists(filePath) ? filePath
                : File.Exists(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", filePath))
                    ? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", filePath)
                    : null;

            if (resolvedPath != null)
            {
                // 即解即用：EncryptedFileReader 自动检测 LFEN 魔数，加密则解密，不加密则直接读
                if (_fileReader != null)
                {
                    content = await _fileReader.ReadAllTextAsync(resolvedPath, ct) ?? "";
                }
                else
                {
                    content = await File.ReadAllTextAsync(resolvedPath, ct);
                }
                System.Diagnostics.Debug.WriteLine($"[StoryLoader] 读取文件: {resolvedPath}, 长度: {content?.Length ?? 0}");
                if (content != null && content.Length < 300)
                    System.Diagnostics.Debug.WriteLine($"[StoryLoader]   >>> 内容疑似被截断: {content}");
            }
            else
            {
                _state.Set($"{StateKeys.Story.ErrorPrefix}{Path.GetFileNameWithoutExtension(filePath)}", $"File not found: {filePath}");
                return null;
            }
        }
        var story = ParseStoryFile(filePath, content ?? "");
        if (story == null) return null;

        // 提取子目录（场景分类）
        var dir = Path.GetDirectoryName(filePath);
        var storyRoot = ResolveDirectory("Stories");
        if (dir != null && storyRoot != null && dir.Length > storyRoot.Length)
        {
            var relativeDir = dir[storyRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(relativeDir))
                story.Directory = relativeDir;
        }

        // 注册 JSON 格式的 define（仅 JSON 格式，DSL define 已由 ExtractSceneBlocks 收集为场景级/全局）
        RegisterDefinesFromJson(story, content ?? "");

        // 从脚本中提取并注册 scene 块，剩余的脚本再编译为流程命令
        System.Diagnostics.Debug.WriteLine($"[StoryLoader] story.Script 前 200 字: {story.Script[..Math.Min(200, story.Script.Length)]}");
        var (sceneBlocks, flowScript, globalDefines) = ExtractSceneBlocks(story.Script);
        System.Diagnostics.Debug.WriteLine($"[StoryLoader] ExtractSceneBlocks 返回 {sceneBlocks.Count} 个场景, 全局 defines: {globalDefines?.Count ?? 0}");

        // 注入全局 defines（顶格 define，加载时即生效，不编译为命令）
        InjectGlobalDefines(globalDefines);
        foreach (var (sceneName, elements, entryScript, defines, layoutMode, sceneType) in sceneBlocks)
        {
            System.Diagnostics.Debug.WriteLine($"[StoryLoader]   >>> 场景 '{sceneName}' 元素数: {elements.Count}");
            var scene = new SceneEntity
            {
                SceneName = sceneName,
                Elements = elements,
                IsTransient = false,
                Defines = defines,
                LayoutMode = layoutMode,
                SceneType = sceneType
            };
            _sceneRegistry.RegisterScene(sceneName, scene);
            System.Diagnostics.Debug.WriteLine($"[StoryLoader] 注册场景: {sceneName}, Elements={elements.Count}");

            // scene 块内的流程命令（say/set/if 等）转为 flow script 中的 label 块
            // 这样它们会通过 DslExecutor.RunAsync 执行，正确等待交互命令
            if (!string.IsNullOrWhiteSpace(entryScript))
            {
                flowScript += $"\nlabel {sceneName}:\n" + entryScript + "\n";
                System.Diagnostics.Debug.WriteLine($"[StoryLoader]   >>> entry script 转为 label [{sceneName}]");
            }
        }

        // Route 注册已移除

        // 验证注册状态
        System.Diagnostics.Debug.WriteLine($"[StoryLoader] SceneRegistry 中注册的场景: {string.Join(", ", _sceneRegistry.RegisteredScenes)}");

        // 编译剩余流程脚本
        System.Diagnostics.Debug.WriteLine($"  场景块: {sceneBlocks.Count} 个, 流程脚本: {flowScript.Length} 字符");
        var result = _dslEngine.Compile(flowScript);
        if (result.Success)
        {
            story.CompiledCommands = result.Commands;
            story.Labels = result.Labels;
        }
        else
        {
            _state.Set($"{StateKeys.Story.ErrorPrefix}{story.Id}", result.Error ?? "Unknown compilation error");
            return story;
        }

        // 按 ID 分组存储（支持多语言版本）
        if (!_loadedStories.TryGetValue(story.Id, out var list))
        {
            list = new List<StoryFile>();
            _loadedStories[story.Id] = list;
        }

        // 替换同语言版本或追加
        var existing = list.FindIndex(s => s.Lang == story.Lang);
        if (existing >= 0)
            list[existing] = story;
        else
            list.Add(story);

        _state.Set($"{StateKeys.Story.LoadedPrefix}{story.Id}", true);

        return story;
    }

    /// <summary>
    /// 从目录批量加载 .story 文件（递归搜索所有子目录）
    /// <para>子目录作为场景分类，如 Stories/chapter1_初端/01.story。</para>
    /// </summary>
    /// <param name="directory">根目录路径（如 "Stories"）</param>
    /// <param name="pattern">搜索模式（默认 "*.story"）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>成功加载的故事文件数量</returns>
    public async ValueTask<int> LoadFromDirectoryAsync(string directory, string pattern = "*.story", CancellationToken ct = default)
    {
        var resolvedDir = ResolveDirectory(directory);
        if (resolvedDir == null) return 0;

        // 递归搜索所有 .story 文件（含子目录）
        var files = Directory.GetFiles(resolvedDir, pattern, SearchOption.AllDirectories);
        var loadedCount = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            var story = await LoadFromFileAsync(file, ct);
            if (story is not null) loadedCount++;
        }

        return loadedCount;
    }

    /// <summary>
    /// 按章节（子目录）加载 .story 文件
    /// <para>如 LoadChapterAsync("Stories", "chapter1_初端") 加载 Stories/chapter1_初端/*.story</para>
    /// </summary>
    /// <param name="baseDirectory">故事根目录</param>
    /// <param name="chapterName">章节子目录名</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>成功加载的故事文件数量</returns>
    public async ValueTask<int> LoadChapterAsync(string baseDirectory, string chapterName, CancellationToken ct = default)
    {
        var resolvedBase = ResolveDirectory(baseDirectory);
        if (resolvedBase == null) return 0;

        var chapterDir = Path.Combine(resolvedBase, chapterName);
        if (!Directory.Exists(chapterDir)) return 0;

        var files = Directory.GetFiles(chapterDir, "*.story", SearchOption.AllDirectories);
        var loadedCount = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            var story = await LoadFromFileAsync(file, ct);
            if (story is not null) loadedCount++;
        }

        return loadedCount;
    }

    /// <summary>
    /// 加载指定场景分类下的所有 .story 文件
    /// <para>与 GetStoriesByDirectory 配合使用。</para>
    /// </summary>
    /// <param name="baseDirectory">故事根目录</param>
    /// <param name="sceneDirectory">场景子目录名</param>
    /// <param name="ct">取消令牌</param>
    public async ValueTask<int> LoadSceneAsync(string baseDirectory, string sceneDirectory, CancellationToken ct = default)
    {
        return await LoadChapterAsync(baseDirectory, sceneDirectory, ct);
    }

    /// <summary>
    /// 获取指定场景分类下的所有故事 ID
    /// </summary>
    public IEnumerable<string> GetStoriesByDirectory(string directory)
    {
        return _loadedStories
            .Where(kv => kv.Value.Any(s =>
                string.Equals(s.Directory, directory, StringComparison.OrdinalIgnoreCase)))
            .Select(kv => kv.Key);
    }

    /// <summary>
    /// 获取所有已加载故事的场景分类列表
    /// </summary>
    public IEnumerable<string> GetDirectories()
    {
        return _loadedStories.Values
            .SelectMany(list => list.Select(s => s.Directory))
            .Where(d => d != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)!;
    }

    /// <summary>
    /// 加载指定故事 ID 的当前语言版本
    /// <para>自动按当前语言从 Stories/ 目录下查找对应 .story 文件。</para>
    /// </summary>
    /// <param name="directory">故事目录</param>
    /// <param name="storyId">故事唯一标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功加载</returns>
    public async ValueTask<bool> LoadForCurrentLanguageAsync(string directory, string storyId, CancellationToken ct = default)
    {
        var filePath = ResolveStoryFile(directory, storyId, _currentLang);
        if (filePath == null)
        {
            _state.Set($"{StateKeys.Story.ErrorPrefix}{storyId}", $"No story file found for ID '{storyId}' in lang '{_currentLang}'");
            return false;
        }

        var story = await LoadFromFileAsync(filePath, ct);
        return story != null;
    }

    /// <summary>
    /// 批量加载目录下所有符合当前语言的故事文件
    /// <para>查找 {id}_{currentLang}.story 模式的文件。</para>
    /// </summary>
    /// <param name="directory">故事目录</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>成功加载的数量</returns>
    public async ValueTask<int> LoadAllForCurrentLanguageAsync(string directory, CancellationToken ct = default)
    {
        if (!Directory.Exists(directory)) return 0;

        var pattern = $"*_{_currentLang}.story";
        var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        var loadedCount = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            var story = await LoadFromFileAsync(file, ct);
            if (story is not null) loadedCount++;
        }

        return loadedCount;
    }

    /// <summary>
    /// 执行已加载的故事（通过 DslExecutor 管理 label/jump/if 等生命周期）
    /// <para>自动选择当前语言版本的故事。</para>
    /// </summary>
    /// <param name="storyId">故事唯一标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>执行是否成功</returns>
    public async ValueTask<bool> ExecuteAsync(string storyId, CancellationToken ct = default)
    {
        if (!_loadedStories.TryGetValue(storyId, out var list) || list.Count == 0)
        {
            _state.Set($"{StateKeys.Story.ErrorPrefix}{storyId}", $"Story not loaded: {storyId}");
            return false;
        }

        // 选择当前语言版本，若无则用第一个
        var story = list.Find(s => s.Lang == _currentLang) ?? list[0];

        if (story.CompiledCommands is null || story.CompiledCommands.Count == 0)
        {
            // 尝试重新编译
            var result = _dslEngine.Compile(story.Script);
            if (!result.Success)
            {
                _state.Set($"{StateKeys.Story.ErrorPrefix}{storyId}", result.Error ?? "Compilation failed");
                return false;
            }
            story.CompiledCommands = result.Commands;
        }

        // 通过 DslExecutor 加载命令列表（由 GameLoop 的 Step 驱动执行）
        if (_dslExecutor != null && story.Labels != null)
        {
            _dslExecutor.LoadCommands(story.CompiledCommands, story.Labels);
        }
        else
        {
            // 没有 DslExecutor 时回退到直接投递
            foreach (var command in story.CompiledCommands)
            {
                if (ct.IsCancellationRequested) return false;
                await _pipeline.SendAsync(command, ct);
            }
        }

        return true;
    }

    /// <summary>
    /// 获取已加载的故事文件（当前语言版本优先）
    /// </summary>
    public StoryFile? GetStory(string storyId)
    {
        if (!_loadedStories.TryGetValue(storyId, out var list) || list.Count == 0)
            return null;

        return list.Find(s => s.Lang == _currentLang) ?? list[0];
    }

    /// <summary>
    /// 获取故事的所有语言版本
    /// </summary>
    public IReadOnlyList<StoryFile> GetAllVersions(string storyId)
    {
        return _loadedStories.TryGetValue(storyId, out var list)
            ? list.AsReadOnly()
            : Array.Empty<StoryFile>();
    }

    /// <summary>
    /// 获取所有已加载的故事 ID
    /// </summary>
    public IEnumerable<string> GetLoadedStoryIds() => _loadedStories.Keys;

    /// <summary>
    /// 清空所有已加载的故事
    /// </summary>
    public void Clear()
    {
        _loadedStories.Clear();
    }

    /// <summary>
    /// 解析 .story 文件内容为 StoryFile 对象
    /// </summary>
    private static StoryFile? ParseStoryFile(string filePath, string content)
    {
        // 从文件名提取 ID 和语言
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var (storyId, lang) = ParseFileName(fileName);

        // 只有以 { 开头的内容才尝试 JSON 解析（纯 DSL 文件不是 JSON）
        var trimmed = content.AsSpan().TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '{')
        {
            try
            {
                var node = JsonNode.Parse(content);
                if (node is not null)
                {
                    var jsonId = GetStringProperty(node, "id") ?? storyId;
                    var jsonLang = GetStringProperty(node, "lang") ?? lang;
                    var jsonScript = GetStringProperty(node, "script");

                    if (jsonScript != null)
                    {
                        // JSON 格式的 story 文件，script 字段包含实际 DSL
                        var dir = ExtractDirectory(filePath);

                        // JSON 支持 defines 字段——结构化的 define 定义
                        // 在 ParseStoryFile 和 RegisterDefinesFromJson 之间传递
                        // 这里通过 story.Defines 暴露给调用方
                        JsonNode? definesNode = null;
                        if (node["defines"] is JsonObject definesObj)
                            definesNode = definesObj;

                        return new StoryFile
                        {
                            Id = jsonId,
                            Title = GetStringProperty(node, "title") ?? jsonId,
                            Script = jsonScript,
                            Author = GetStringProperty(node, "author"),
                            SceneName = GetStringProperty(node, "sceneName"),
                            Lang = jsonLang,
                            Directory = GetStringProperty(node, "directory") ?? dir,
                            DefinesNode = definesNode
                        };
                    }
                }
            }
            catch (JsonException)
            {
                // JSON 解析失败，当作纯 DSL
            }
        }

        // 纯 DSL 降级
        return new StoryFile
        {
            Id = storyId,
            Title = storyId,
            Script = content,
            Lang = lang,
            Directory = ExtractDirectory(filePath)
        };
    }

    /// <summary>
    /// 从文件名解析故事 ID 和语言代码
    /// <para>规则：chapter1_zh-CN.story → Id=chapter1, Lang=zh-CN</para>
    /// <para>规则：chapter1.story → Id=chapter1, Lang=null</para>
    /// </summary>
    private static (string id, string? lang) ParseFileName(string fileNameWithoutExt)
    {
        // 检查是否包含语言后缀：chapter1_zh-CN
        var lastUnderscore = fileNameWithoutExt.LastIndexOf('_');
        if (lastUnderscore > 0 && lastUnderscore < fileNameWithoutExt.Length - 1)
        {
            var potentialLang = fileNameWithoutExt[(lastUnderscore + 1)..];
            // 语言代码格式：xx-XX 或 xx
            if (potentialLang.Length == 5 && potentialLang[2] == '-')
                return (fileNameWithoutExt[..lastUnderscore], potentialLang);
            if (potentialLang.Length == 2)
                return (fileNameWithoutExt[..lastUnderscore], potentialLang);
        }

        return (fileNameWithoutExt, null);
    }

    /// <summary>
    /// 从 JsonNode 中读取字符串属性
    /// </summary>
    private static string? GetStringProperty(JsonNode node, string propertyName)
    {
        if (node[propertyName] is JsonValue value && value.TryGetValue<string>(out var str))
            return str;
        return null;
    }

    /// <summary>
    /// 解析故事目录路径
    /// <para>搜索顺序：传入路径 → 输出目录下的同名子目录 → 项目根目录下的同名子目录</para>
    /// </summary>
    private static string? ResolveDirectory(string directory)
    {
        // 1. 直接存在
        if (Directory.Exists(directory))
            return directory;

        var dirName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // 2. 输出目录下
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var outputDir = Path.Combine(baseDir, dirName);
            if (Directory.Exists(outputDir))
                return outputDir;
        }

        // 3. 项目根目录下（向上搜索直到找到 .csproj 或 slnx）
        var current = Path.GetFullPath(".");
        for (int i = 0; i < 5; i++)
        {
            var probe = Path.Combine(current, dirName);
            if (Directory.Exists(probe))
                return probe;

            var parent = Path.GetDirectoryName(current);
            if (parent == null || parent == current) break;
            current = parent;
        }

        return null;
    }

    /// <summary>
    /// 注册 JSON 格式的 define 到状态容器
    /// <para>仅处理 JSON story 文件的 defines 字段。DSL 格式的 define 已由 ExtractSceneBlocks 解析为场景级 Defines，</para>
    /// <para>在导航到对应场景时通过 MergeIntoState 深合并注入（"你不认识他之前，他不存在于你的世界"）。</para>
    /// </summary>
    public void RegisterDefinesFromJson(StoryFile story, string rawContent)
    {
        // JSON 格式：读取 story.DefinesNode（文件级共享 define，加载时即注入）
        if (story.DefinesNode is JsonObject definesObj)
        {
            foreach (var (key, val) in definesObj)
            {
                if (key == null || val == null) continue;
                var parsed = JsonNodeToObject(val);
                _state.Set(key, parsed);
                System.Diagnostics.Debug.WriteLine($"[StoryLoader] define (JSON): {key} = {parsed}");
            }
        }

        // DSL 格式的 define 已由 ExtractSceneBlocks 收集为场景级/全局 Defines，此处不再扫描文件级 define 行
    }

    /// <summary>
    /// 注入全局 defines（顶格 define，加载时即生效）
    /// <para>遵循 define...once 语义：仅当键不存在时设置（与 SetVariableHandler.IsDefine 行为一致）。</para>
    /// <para>由 LoadFromFileAsync 和 StoryRegistry.LoadScene 调用。</para>
    /// </summary>
    public void InjectGlobalDefines(Dictionary<string, object?>? globalDefines)
    {
        if (globalDefines == null) return;
        foreach (var (key, val) in globalDefines)
        {
            if (!_state.ContainsKey(key))
            {
                _state.Set(key, val);
                System.Diagnostics.Debug.WriteLine($"[StoryLoader] global define 注入: {key} = {val}");
            }
        }
    }

    /// <summary>
    /// 将 JsonNode 递归转为 object（支持嵌套字典/列表）
    /// </summary>
    private static object? JsonNodeToObject(JsonNode? node)
    {
        if (node == null) return null;
        if (node is JsonValue jv)
        {
            // 尝试解析为数字、布尔、字符串
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<double>(out var d)) return d;
            if (jv.TryGetValue<bool>(out var b)) return b;
            if (jv.TryGetValue<string>(out var s)) return s;
            return jv.ToString();
        }
        if (node is JsonObject jo)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var (k, v) in jo)
            {
                if (k != null)
                    dict[k] = JsonNodeToObject(v);
            }
            return dict;
        }
        if (node is JsonArray ja)
        {
            var list = new List<object?>();
            foreach (var item in ja)
                list.Add(JsonNodeToObject(item));
            return list;
        }
        return node.ToString();
    }

    /// <summary>
    /// 解析 set/define 的值字符串为具体类型
    /// </summary>
    private static object? ParseSetValue(string val)
    {
        // 用 LingFanDslEngine 的现有实现，但它是实例方法
        // 此处简化：数字/布尔/字符串的简单解析
        if (bool.TryParse(val, out var b)) return b;
        if (int.TryParse(val, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        // 去掉两端引号
        if (val.Length >= 2 && val[0] == '"' && val[^1] == '"') return val[1..^1];
        return val;
    }

    /// <summary>
    /// 从文件路径中提取 Stories/ 下的子目录名
    /// <para>如 "Stories/chapter1_初端/01.story" → "chapter1_初端"</para>
    /// </summary>
    private static string? ExtractDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir)) return null;

        // 找到 "Stories" 之后的路径作为子目录
        var idx = dir.IndexOf("Stories", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var relative = dir[(idx + "Stories".Length)..]
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(relative) ? null : relative;
    }

    /// <summary>
    /// 从 DSL 脚本中提取 scene 块，返回（场景名列表，剩余流程脚本）
    /// <para>每个 scene 块内的 define 行被解析为场景级 Defines（导航时深合并）。</para>
    /// <para>支持 scene 行属性：scene "xxx" layout=canvas columns="*,2*"</para>
    /// <para>支持缩进嵌套：容器类型元素的缩进行作为子元素。</para>
    /// </summary>
public (List<(string SceneName, List<UIElementEntity> Elements, string EntryScript, Dictionary<string, object?>? Defines, string LayoutMode, SceneType SceneType)> Scenes, string FlowScript, Dictionary<string, object?>? GlobalDefines)
ExtractSceneBlocks(string script)
    {
        var scenes = new List<(string, List<UIElementEntity>, string, Dictionary<string, object?>?, string, SceneType)>();
        var flowLines = new List<string>();
        Dictionary<string, object?>? globalDefines = null;
        var lines = script.Split('\n');
        bool inSceneBlock = false;
        string? currentSceneName = null;
        string currentLayoutMode = "grid";
        SceneType currentSceneType = SceneType.Game;
        var currentElements = new List<UIElementEntity>();
        var currentEntryScript = new List<string>();
        Dictionary<string, object?>? currentDefines = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // scene "xxx" 开始（仅顶格行识别为场景定义，缩进的 scene 是 DSL 导航命令）
            // 快速前缀过滤 + Pidgin 解析器
            var lineIndent = 0;
            foreach (var ch in rawLine)
            {
                if (ch == ' ') lineIndent++;
                else if (ch == '\t') lineIndent += 4;
                else break;
            }
            var trimmedForScene = line.AsSpan().TrimStart();
            if (lineIndent == 0 && (trimmedForScene.StartsWith("scene ") || trimmedForScene.StartsWith("scene\t")))
            {
                var sceneHeader = DslParser.ParseSceneHeader(line);
                if (sceneHeader != null && !line.Contains("navigate"))
                {
                    var sceneNameVal = sceneHeader.SceneName;
                    var layoutMode = sceneHeader.LayoutMode;
                    var sceneType = sceneHeader.SceneType;

                    System.Diagnostics.Debug.WriteLine($"[ExtractSceneBlocks] 匹配 scene: \"{sceneNameVal}\" layout={layoutMode} type={sceneType}");
                    // 之前的 scene 块结束，保存
                    if (inSceneBlock && currentSceneName != null)
                    {
                        var entryScript = string.Join("\n", currentEntryScript);
                        scenes.Add((currentSceneName, currentElements, entryScript, currentDefines, currentLayoutMode, currentSceneType));
                    }
                    inSceneBlock = true;
                    currentSceneName = sceneNameVal;
                    currentLayoutMode = layoutMode;
                    currentSceneType = sceneType;
                    currentElements = new List<UIElementEntity>();
                    currentEntryScript = new List<string>();
                    currentDefines = null;
                    continue;
                }
            }

            if (inSceneBlock)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith('#'))
                    continue;

                // 计算缩进空格数（Tab 按 4 空格计算）
                var indent = 0;
                foreach (var ch in rawLine)
                {
                    if (ch == ' ') indent++;
                    else if (ch == '\t') indent += 4;
                    else break;
                }

                // 检查缩进：顶格行（非缩进）且不是 UI 元素 → 退出 scene 块
                var isTopLevel = indent == 0;
                if (isTopLevel && !IsUiElementLine(trimmed))
                {
                    // 顶格非元素行——退出 scene 块
                    if (currentSceneName != null)
                    {
                        var entryScript = string.Join("\n", currentEntryScript);
                        scenes.Add((currentSceneName, currentElements, entryScript, currentDefines, currentLayoutMode, currentSceneType));
                        System.Diagnostics.Debug.WriteLine($"[ExtractSceneBlocks] 保存 scene: {currentSceneName}, 元素数: {currentElements.Count}, entry: {currentEntryScript.Count} 行, defines: {currentDefines?.Count ?? 0}");
                    }
                    System.Diagnostics.Debug.WriteLine($"[ExtractSceneBlocks] 退出 scene 块 at 顶格行: [{line.Trim()}]");
                    inSceneBlock = false;
                    currentSceneName = null;
                    currentElements = new List<UIElementEntity>();
                    currentEntryScript = new List<string>();
                    currentDefines = null;
                    currentSceneType = SceneType.Game;

                    // 退出 scene 块的顶格行如果是 define，收集为全局 defines（不进入 flowScript）
                    var exitTrimmed = line.Trim();
                    if (exitTrimmed.StartsWith("define ") || exitTrimmed.StartsWith("define\t"))
                    {
                        var exitDefineEntry = DslParser.ParseDefineLine(exitTrimmed);
                        if (exitDefineEntry != null)
                        {
                            var parsed = ParseDefineValue(exitDefineEntry.RawValue);
                            globalDefines ??= new Dictionary<string, object?>();
                            globalDefines[exitDefineEntry.Key] = parsed;
                            System.Diagnostics.Debug.WriteLine($"[ExtractSceneBlocks] global define (scene exit): {exitDefineEntry.Key} = {parsed}");
                            continue;
                        }
                    }

                    flowLines.Add(line);
                    continue;
                }

                // scene 块内的 define 行 → 解析为场景级变量定义（不进入 entry script）
                // 快速前缀过滤 + Pidgin 解析器
                if (trimmed.StartsWith("define ") || trimmed.StartsWith("define\t"))
                {
                    var defineEntry = DslParser.ParseDefineLine(trimmed);
                    if (defineEntry != null)
                    {
                        var parsed = ParseDefineValue(defineEntry.RawValue);
                        currentDefines ??= new Dictionary<string, object?>();
                        currentDefines[defineEntry.Key] = parsed;
                        System.Diagnostics.Debug.WriteLine($"[ExtractSceneBlocks] scene define: {defineEntry.Key} = {parsed}");
                        continue;
                    }
                }

                // 所有 scene 块内非 define 行（UI 元素 + 流程命令）统一加入 entryScript
                // UI 元素行（image/text/button 等）会被 DslStatementParser 解析为 ShowElementStmt
                // 流程命令行（say/set/transition 等）保持原有解析
                // 这样保证了元素与流程命令的交织顺序，实现按序揭示
                currentEntryScript.Add(line);
            }
            else
            {
                // 顶格 define 行 → 收集为全局 defines（加载时直接注入状态容器，不编译为命令）
                var trimmedFlow = line.Trim();
                if (trimmedFlow.StartsWith("define ") || trimmedFlow.StartsWith("define\t"))
                {
                    var defineEntry = DslParser.ParseDefineLine(trimmedFlow);
                    if (defineEntry != null)
                    {
                        var parsed = ParseDefineValue(defineEntry.RawValue);
                        globalDefines ??= new Dictionary<string, object?>();
                        globalDefines[defineEntry.Key] = parsed;
                        System.Diagnostics.Debug.WriteLine($"[ExtractSceneBlocks] global define: {defineEntry.Key} = {parsed}");
                        continue;
                    }
                }

                flowLines.Add(line);
            }
        }

        // 末尾的未闭合 scene 块
        if (inSceneBlock && currentSceneName != null)
        {
            var entryScript = string.Join("\n", currentEntryScript);
            scenes.Add((currentSceneName, currentElements, entryScript, currentDefines, currentLayoutMode, currentSceneType));
        }

        return (scenes, string.Join("\n", flowLines), globalDefines);
    }

    /// <summary>
    /// 已知的 UI 元素类型集合——用于区分 scene 块内的元素行和顶格流程命令行。
    /// 当一行顶格且首词在此集合中时，视为 scene 块内元素而非退出。
    /// <para>注意：label 和 menu 是 DSL 流程控制关键字（label xxx: / menu "..."），
    /// 不可放入此集合，否则 scene 块无法在遇到 label/menu 行时正确退出。</para>
    /// </summary>
    private static readonly HashSet<string> s_uiElementTypes = new(StringComparer.OrdinalIgnoreCase)
    {
"text", "button", "image", "panel", "grid", "stack", "canvas",
"border", "frame", "scroll", "scrollviewer", "viewport", "background", "bar", "vbar", "slider",
"checkbox", "imagebutton", "separator", "spacer", "narrator",
"speaker", "dialog", "choice", "progress", "progressbar",
"toggle", "switch", "combobox", "dropdown", "listbox", "treeview",
"tab", "tabitem", "menubar", "tooltip", "textbox", "passwordbox",
"vbox", "hbox"
    };

    /// <summary>
    /// 判断一行是否为 UI 元素（首词在 s_uiElementTypes 中）
    /// </summary>
    private static bool IsUiElementLine(string trimmed)
    {
        var spaceIdx = trimmed.IndexOf(' ');
        var firstWord = spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
        return s_uiElementTypes.Contains(firstWord);
    }

    /// <summary>
    /// 解析 define 值字符串为具体类型（数字/布尔/字符串/JSON对象/数组）
    /// </summary>
    private static object? ParseDefineValue(string val)
    {
        if (bool.TryParse(val, out var b)) return b;
        if (int.TryParse(val, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        // 去掉两端引号
        if (val.Length >= 2 && val[0] == '"' && val[^1] == '"') return val[1..^1];
        // 尝试 JSON 对象/数组
        if (val.StartsWith('{') || val.StartsWith('['))
        {
            try
            {
                var node = JsonNode.Parse(val);
                return JsonNodeToObject(node);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[StoryLoader] JSON parse failed: {ex.Message}"); }
        }
        return val;
    }

    /// <summary>
    /// 解析 scene 块内的 UI 元素行（委托给 Pidgin 解析器）
    /// <para>支持属性任意顺序，at/size 语法糖自动展开。</para>
    /// </summary>
    internal static UIElementEntity? ParseSceneElement(string line)
    {
        return DslParser.ParseElement(line);
    }

    // Route 已移除
    private static void ExtractRouteBlocksRemoved() { }
}