using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.DslCore;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;
using UiElementEntity = LingFanEngine.Abstractions.Entities.UIs.UIElementEntity;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 翻译同步服务实现。
/// <para>扫描 <c>Resources/Stories/**/*.story</c>（DSL）+ C# 全部可翻译文本（带来源），
/// 按所选布局（<see cref="TranslationLayout"/> Flat/Mirrored/SingleFile）路由到目标文件，
/// 每文件以"增量 diff + <see cref="IFileEditor"/> 原子写"维护 <c>Lang/{lang}/</c>。
/// <see cref="PrepareSyncAsync"/> 只准备编辑（不落盘），经整轮审批后由 <see cref="ApplyEditsAsync"/> 提交。</para>
/// </summary>
public class TranslationService : ITranslationService
{
    private readonly IFileEditor _fileEditor;

    /// <summary>
    /// 写入翻译文件用的字典序列化元数据：源生成（AOT 安全）+ 宽松 JSON 编码（保留中文/原文，不转义成 \uXXXX；
    /// emoji 等代理对由 <see cref="RestoreSurrogatePairs"/> 在序列化后还原）。
    /// </summary>
    private static readonly JsonTypeInfo<Dictionary<string, string>> s_translationTypeInfo =
        (JsonTypeInfo<Dictionary<string, string>>)new JsonSerializerOptions(SdkJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }.GetTypeInfo(typeof(Dictionary<string, string>));

    /// <summary>匹配 JSON 中 emoji/非 BMP 字符的 "代理对转义"（如 \uD83D\uDD27），以便还原为原始字符。</summary>
    private static readonly System.Text.RegularExpressions.Regex s_surrogateEscapeRegex = new(
        @"\\u([dD][89a-fA-F][0-9a-fA-F]{2})\\u([dD][c-fC-F][0-9a-fA-F]{2})",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>把序列化结果中的代理对转义（emoji 等）还原为原始 UTF-8 字符，保持可读。</summary>
    private static string RestoreSurrogatePairs(string json)
        => s_surrogateEscapeRegex.Replace(json, m =>
        {
            var hi = Convert.ToInt32(m.Groups[1].Value, 16);
            var lo = Convert.ToInt32(m.Groups[2].Value, 16);
            return char.ConvertFromUtf32(((hi - 0xD800) << 10) + (lo - 0xDC00) + 0x10000);
        });

    /// <summary>创建翻译服务（注入文件编辑器，走原子写/备份/回滚）。</summary>
    public TranslationService(IFileEditor fileEditor)
    {
        _fileEditor = fileEditor ?? throw new ArgumentNullException(nameof(fileEditor));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ScannedText>> ScanTranslatableTextsAsync(string projectDir)
        => Task.Run(() => (IReadOnlyList<ScannedText>)ScanTranslatableTexts(projectDir));

    /// <inheritdoc/>
    public Task<TranslationSyncResult> PrepareSyncAsync(
        string projectDir, string lang, TranslationLayout layout, ITranslator translator,
        IProgress<TranslationProgress>? progress, bool forceRetranslate, string sourceLang, CancellationToken ct)
        => Task.Run(() => PrepareSyncCoreAsync(projectDir, lang, layout, translator, progress, forceRetranslate, sourceLang, ct), ct);

    /// <inheritdoc/>
    public async Task<int> ApplyEditsAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var applied = 0;
        foreach (var edit in edits)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await _fileEditor.ApplyAsync(edit, ct).ConfigureAwait(false);
                applied++;
            }
            catch
            {
                // 部分失败：回滚已提交项，尽力恢复一致态
                await RollbackEditsAsync(edits.Take(applied).ToList(), ct).ConfigureAwait(false);
                throw;
            }
        }
        return applied;
    }

    /// <inheritdoc/>
    public async Task<int> RollbackEditsAsync(IReadOnlyList<FileEdit> edits, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var rolled = 0;
        foreach (var edit in edits.Reverse())
        {
            ct.ThrowIfCancellationRequested();
            if (await _fileEditor.RollbackAsync(edit, ct).ConfigureAwait(false))
                rolled++;
        }
        return rolled;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, bool>> GetTranslationStatusAsync(string projectDir, string lang)
        => Task.Run(() => GetTranslationStatus(projectDir, lang));

    // ===== 内部实现 =====

    // 语言根目录：<项目根>/Resources/Lang（与模板 V1 示例位置一致；<项目根>/Lang 是旧 SDK 误写位置，仅读兼容）
    private static string LangDir(string projectDir)
        => Path.Combine(projectDir, ProjectConstants.ResourcesDir, ProjectConstants.LangDir);

    private static string StoriesDir(string projectDir)
        => Path.Combine(projectDir, ProjectConstants.ResourcesDir, ProjectConstants.StoriesDir);

    private static string RootStoriesDir(string projectDir)
        => Path.Combine(projectDir, ProjectConstants.StoriesDir);

    /// <summary>扫描：返回带来源条目（跨文件不去重——Mirrored 需要逐 story 保留）。</summary>
    private static List<ScannedText> ScanTranslatableTexts(string projectDir)
    {
        var list = new List<ScannedText>();

        // 标准两个故事根 + 兜底任意 **/Stories
        var primary = new List<string>();
        foreach (var c in new[] { StoriesDir(projectDir), RootStoriesDir(projectDir) })
            if (Directory.Exists(c))
                primary.Add(c);

        var storyFound = 0;
        foreach (var dir in primary)
            storyFound += ScanStoryDir(dir, list);

        if (storyFound == 0 && Directory.Exists(projectDir))
        {
            foreach (var dir in Directory.GetDirectories(projectDir, ProjectConstants.StoriesDir, SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(dir))
                    continue;
                ScanStoryDir(dir, list);
            }
        }

        // C# 文本：继承 StoryScript 的类按类名归文件（SourceStory=类名，与 story 路由一致）；
        // 不继承的（引擎通用 UI）SourceStory=null，归 main.json。均跨源统一按“文本”去重。
        if (Directory.Exists(projectDir))
        {
            var uiSeen = new HashSet<string>(StringComparer.Ordinal);       // main.json 全局去重
            var classSeen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal); // 每个 StoryScript 类内去重
            foreach (var file in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(file))
                    continue;
                string source;
                try { source = File.ReadAllText(file); }
                catch { continue; }

                var storyClasses = FindStoryScriptClasses(source);
                foreach (var (literal, line) in ExtractCSharpStringLiteralsWithLine(source))
                {
                    if (!ContainsCjk(literal))
                        continue;
                    var cls = ClassForLine(storyClasses, line); // 该字面量所属的 StoryScript 类名，无则 null
                    if (cls is null)
                    {
                        if (uiSeen.Add(literal))
                            list.Add(new ScannedText(literal, ScannedTextKind.Ui, null));
                    }
                    else
                    {
                        if (!classSeen.TryGetValue(cls, out var seen))
                            classSeen[cls] = seen = new HashSet<string>(StringComparer.Ordinal);
                        if (seen.Add(literal))
                            list.Add(new ScannedText(literal, ScannedTextKind.Ui, cls));
                    }
                }
            }
        }

        return list;
    }

    // ===== C# 类继承 StoryScript 识别 =====

    private static readonly System.Text.RegularExpressions.Regex s_classDeclRegex = new(
        @"(?m)^\s*(?:(?:public|internal|private|protected|static|abstract|sealed|partial|async|unsafe|readonly|ref)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([^{]+))?",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>返回继承 StoryScript 的类及其声明起始行（支持同一文件内间接继承传递）。</summary>
    private static List<(string Name, int Line)> FindStoryScriptClasses(string source)
    {
        var decls = new List<(string Name, string Base, int Line)>();
        foreach (System.Text.RegularExpressions.Match m in s_classDeclRegex.Matches(source))
        {
            decls.Add((m.Groups[1].Value, m.Groups[2].Value ?? "", source[..m.Index].Count(c => c == '\n')));
        }

        var isStory = new HashSet<string>(StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (name, baseText, _) in decls)
            {
                if (isStory.Contains(name))
                    continue;
                // 直接或经本文件内 StoryScript 子类间接继承
                var inheritsStory = baseText.Contains("StoryScript", StringComparison.OrdinalIgnoreCase)
                    || decls.Any(d => isStory.Contains(d.Name) && System.Text.RegularExpressions.Regex.IsMatch(baseText, $@"\b{d.Name}\b"));
                if (inheritsStory)
                {
                    isStory.Add(name);
                    changed = true;
                }
            }
        }

        return decls.Where(d => isStory.Contains(d.Name))
                    .Select(d => (d.Name, d.Line))
                    .OrderBy(d => d.Line)
                    .ToList();
    }

    /// <summary>返回 <paramref name="line"/> 所属的 StoryScript 类名（取最近一个声明起始行 &lt;= 该行的类）。</summary>
    private static string? ClassForLine(List<(string Name, int Line)> storyClasses, int line)
    {
        string? best = null;
        var bestLine = -1;
        foreach (var (name, declLine) in storyClasses)
        {
            if (declLine <= line && declLine > bestLine)
            {
                best = name;
                bestLine = declLine;
            }
        }
        return best;
    }

    private static int ScanStoryDir(string dir, List<ScannedText> list)
    {
        var count = 0;
        foreach (var file in Directory.GetFiles(dir, "*.story", SearchOption.AllDirectories))
        {
            var relStory = Path.GetRelativePath(dir, file).Replace('\\', '/'); // 如 title/title_main.story
            CollectStoryTexts(file, relStory, list);
            count++;
        }
        return count;
    }

    private static void CollectStoryTexts(string storyFile, string relStory, List<ScannedText> list)
    {
        string[] lines;
        try { lines = File.ReadAllLines(storyFile); }
        catch { return; }

        var fileSeen = new HashSet<string>(StringComparer.Ordinal); // 同文件内按原文去重
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var stmt = DslStatementParser.ParseLine(line, i);
            if (stmt == null)
                continue;

            switch (stmt)
            {
                case SayStmt say: AddStory(fileSeen, list, say.Text, relStory); break;
                case MenuStmt menu: AddStory(fileSeen, list, menu.Prompt, relStory); break;
                case MenuOptionStmt option: AddStory(fileSeen, list, option.Text, relStory); break;
                case InputStmt input:
                    AddStory(fileSeen, list, input.Prompt, relStory);
                    if (input.Options != null)
                        foreach (var opt in input.Options)
                            AddStory(fileSeen, list, opt, relStory);
                    break;
                case NotifyStmt notify: AddStory(fileSeen, list, notify.Text, relStory); break;
                case SaveStmt save: AddStory(fileSeen, list, save.Title, relStory); break;
                case ShowElementStmt element: CollectElementText(fileSeen, list, element.Element, relStory); break;
            }
        }
    }

    private static void AddStory(HashSet<string> seen, List<ScannedText> list, string? text, string relStory)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (!seen.Add(text))
            return; // 同文件同原文只记一次
        list.Add(new ScannedText(text, ScannedTextKind.Story, relStory));
    }

    private static void CollectElementText(HashSet<string> seen, List<ScannedText> list, UiElementEntity element, string relStory)
    {
        if (element == null)
            return;

        var type = element.ElementType?.ToLowerInvariant() ?? "";
        if (type is "image" or "background" or "portrait" or "video" or "canvas" or "vbar" or "slider" or "panel" or "minigame" or "live2d")
            return;

        if (element.Properties != null && element.Properties.TryGetValue("text", out var text) && text != null)
            if (seen.Add(text.ToString()!))
                list.Add(new ScannedText(text.ToString()!, ScannedTextKind.Story, relStory));

        if (element.Children != null)
            foreach (var child in element.Children)
                CollectElementText(seen, list, child, relStory);
    }

    // ===== C# 扫描 =====

    private static bool ContainsCjk(string s)
    {
        foreach (var ch in s)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF) return true;
            if (ch >= 0x3400 && ch <= 0x4DBF) return true;
            if (ch >= 0xF900 && ch <= 0xFAFF) return true;
        }
        return false;
    }

    /// <summary>提取 C# 源码中的字符串字面量（去注释、处理转义、支持 verbatim/插值）。</summary>
    public static IEnumerable<string> ExtractCSharpStringLiterals(string source)
        => ExtractCSharpStringLiteralsWithLine(source).Select(t => t.Text);

    /// <summary>逐字面量附带所在源码行号（0-based），供按 C# 类范围归类。</summary>
    private static IEnumerable<(string Text, int Line)> ExtractCSharpStringLiteralsWithLine(string source)
    {
        var results = new List<(string Text, int Line)>();
        var sb = new StringBuilder();
        var i = 0;
        var n = source.Length;
        var inString = false;
        var isVerbatim = false;
        var isInterpolated = false;
        var braceDepth = 0;
        var discard = false;

        while (i < n)
        {
            var c = source[i];

            if (!inString && c == '/' && i + 1 < n)
            {
                if (source[i + 1] == '/') { i += 2; while (i < n && source[i] != '\n') i++; continue; }
                if (source[i + 1] == '*') { i += 2; while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/')) i++; i += 2; continue; }
            }

            if (!inString)
            {
                if (c == '@' && i + 1 < n && source[i + 1] == '"') { inString = true; isVerbatim = true; isInterpolated = false; i += 2; continue; }
                if (c == '$' && i + 1 < n && source[i + 1] == '@' && i + 2 < n && source[i + 2] == '"') { inString = true; isVerbatim = true; isInterpolated = true; i += 3; continue; }
                if (c == '@' && i + 1 < n && source[i + 1] == '$' && i + 2 < n && source[i + 2] == '"') { inString = true; isVerbatim = true; isInterpolated = true; i += 3; continue; }
                if (c == '$' && i + 1 < n && source[i + 1] == '"') { inString = true; isVerbatim = false; isInterpolated = true; i += 2; continue; }
                if (c == '"') { inString = true; isVerbatim = false; isInterpolated = false; i += 1; continue; }
                i++;
                continue;
            }

            if (isInterpolated && braceDepth == 0 && c == '{')
            {
                if (i + 1 < n && source[i + 1] == '{') { sb.Append(c); i += 2; continue; }
                braceDepth = 1; discard = true; i++;
                continue;
            }
            if (isInterpolated && braceDepth > 0)
            {
                if (c == '{') { braceDepth++; i++; continue; }
                if (c == '}') { braceDepth--; i++; continue; }
                if (c == '"') { i++; while (i < n && source[i] != '"') { if (source[i] == '\\') i++; i++; } i++; continue; }
                i++;
                continue;
            }
            if (isVerbatim && c == '"')
            {
                if (i + 1 < n && source[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                Flush();
                inString = false; isVerbatim = false; isInterpolated = false; discard = false;
                i++;
                continue;
            }
            if (!isVerbatim && c == '\\' && i + 1 < n && !isInterpolated)
            {
                sb.Append(source[i + 1]);
                i += 2;
                continue;
            }
            if (c == '"')
            {
                Flush();
                inString = false; isVerbatim = false; isInterpolated = false; discard = false;
                i++;
                continue;
            }

            sb.Append(c);
            i++;
        }
        if (inString)
            Flush();
        return results;

        void Flush()
        {
            if (sb.Length > 0 && !discard)
                results.Add((sb.ToString(), LineAt(source, i)));
            sb.Clear();
        }

        static int LineAt(string s, int pos) => pos > s.Length ? s.Count(c => c == '\n') : s.AsSpan(0, pos).Count('\n');
    }

    // ===== 路由 + 同步（准备） =====

    /// <summary>重复文本抽到公共文件 common.json 的阈值：同一文本出现在 N 个不同来源（story/类）即视为公共。</summary>
    private const int CommonThreshold = 2;

    private static Dictionary<string, List<ScannedText>> RouteByLayout(
        List<ScannedText> scanned, string langDir, string lang, TranslationLayout layout)
    {
        var groups = new Dictionary<string, List<ScannedText>>(StringComparer.Ordinal);

        if (layout == TranslationLayout.SingleFile)
        {
            // 单文件布局本身就是一个文件，无需抽 common
            groups[Path.Combine(langDir, lang + ".json")] = new List<ScannedText>(scanned);
            return groups;
        }

        // 统计每个文本出现的“不同来源”数量（决定其是否去 common.json）。
        // 同来源内已去重；此处看文本跨了多少个不同来源。
        var sourceSpread = scanned
            .GroupBy(t => t.Text)
            .ToDictionary(g => g.Key, g => g.Select(t => t.SourceStory).Distinct().Count(), StringComparer.Ordinal);

        var commonFile = Path.Combine(langDir, lang, "common.json");
        var mainFile = Path.Combine(langDir, lang, "main.json");
        foreach (var t in scanned)
        {
            string file;
            if (sourceSpread[t.Text] >= CommonThreshold)
            {
                // 重复出现的文本 → 公共文件（避免每个 story 文件里重复副本）
                file = commonFile;
            }
            else
            {
                file = t switch
                {
                    // SourceStory==null：无归属的 C# 引擎通用 UI / 无来源文本 → main.json
                    { SourceStory: null } => mainFile,
                    // SourceStory 非空（story 相对路径 或 StoryScript 类名）：按来源路由/镜像
                    _ when layout == TranslationLayout.Mirrored => MirroredTarget(langDir, lang, t.SourceStory!),
                    _ => Path.Combine(langDir, lang, Path.GetFileNameWithoutExtension(t.SourceStory!) + ".json"),
                };
            }

            if (!groups.TryGetValue(file, out var gl))
                groups[file] = gl = new List<ScannedText>();
            gl.Add(t);
        }
        return groups;
    }

    private static string MirroredTarget(string langDir, string lang, string sourceStory)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourceStory);
        var relDir = Path.GetDirectoryName(sourceStory);
        return string.IsNullOrEmpty(relDir)
            ? Path.Combine(langDir, lang, baseName + ".json")
            : Path.Combine(langDir, lang, relDir, baseName + ".json");
    }

    private sealed class PerFile
    {
        public Dictionary<string, string> Merged { get; init; } = new(StringComparer.Ordinal);
        public string? OldContent { get; init; }
    }

    private async Task<TranslationSyncResult> PrepareSyncCoreAsync(
        string projectDir, string lang, TranslationLayout layout, ITranslator translator,
        IProgress<TranslationProgress>? progress, bool forceRetranslate, string sourceLang, CancellationToken ct)
    {
        var result = new TranslationSyncResult();
        var scanned = ScanTranslatableTexts(projectDir);
        result.Scanned = scanned.Select(s => s.Text).Distinct(StringComparer.Ordinal).Count();

        var langDir = LangDir(projectDir);
        result.OutputPath = Path.Combine(langDir, lang);

        // 已有译文（跨整个语言根的“已翻 orical”，保证布局切换不重翻）
        var existingAll = LoadExistingAcrossLang(langDir, lang);

        var groups = RouteByLayout(scanned, langDir, lang, layout);
        var targetFiles = groups.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        // 每文件：保留自有键 + 收集待翻（按"唯一文本"聚合，跨文件去重，避免同一句在多个文件被重复翻译）
        var perFile = new Dictionary<string, PerFile>(StringComparer.Ordinal);
        var pendingTexts = new List<string>();                                        // 待翻的唯一文本（保序）
        var textToFiles = new Dictionary<string, List<string>>(StringComparer.Ordinal); // text → 需要该译文的所有文件
        foreach (var file in targetFiles)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = await _fileEditor.ReadAsync(file, ct).ConfigureAwait(false);
            var ownDict = snapshot.Exists ? ReadDict(snapshot.Content) : new Dictionary<string, string>(StringComparer.Ordinal);
            var pf = new PerFile { Merged = ownDict, OldContent = snapshot.Exists ? snapshot.Content : null };
            perFile[file] = pf;

            foreach (var text in groups[file].Select(g => g.Text).Distinct(StringComparer.Ordinal))
            {
                var already = IsTranslated(existingAll, text);
                if (!forceRetranslate && already)
                {
                    result.Kept++;
                    // 用权威译文直接覆盖文件旧值（残留空串/旧回显），保证同文本各文件一致且不留旧占位
                    pf.Merged[text] = existingAll[text];
                    continue;
                }
                if (!textToFiles.TryGetValue(text, out var files))
                {
                    files = new List<string>();
                    textToFiles[text] = files;
                    pendingTexts.Add(text);
                }
                files.Add(file);
            }
        }

        // 一次性批译全部待翻的唯一文本（跨模块高效批量；同一文本只翻一次，结果广播到它的所有文件）
        if (pendingTexts.Count > 0)
        {
            // 让 LLM 翻译器把每批进度实时回传给宿主（其余 Manual/Api 翻译器无进度，忽略）
            var llm = translator as LlmTranslator;
            IProgress<TranslationProgress>? priorProgress = null;
            if (llm != null)
            {
                priorProgress = llm.Progress;
                llm.Progress = progress;
            }
            IReadOnlyList<string?> translations;
            try
            {
                translations = await translator.TranslateBatchAsync(pendingTexts, lang, sourceLang, ct).ConfigureAwait(false);
            }
            finally
            {
                if (llm != null) llm.Progress = priorProgress;
            }

            for (var k = 0; k < pendingTexts.Count; k++)
            {
                ct.ThrowIfCancellationRequested();
                var text = pendingTexts[k];
                var tr = translations.Count > k ? translations[k] : null;

                // 三态判定：null→回退、空→留空、回显原文→留空待补、真译→采纳
                string value;
                if (tr is null)
                {
                    var fallback = existingAll.TryGetValue(text, out var ev) && !string.IsNullOrWhiteSpace(ev) ? ev : text;
                    value = fallback;
                    result.Failed++;
                }
                else if (tr.Length == 0)
                {
                    value = ""; // Manual/生成占位，译文留空待外部填充
                }
                else if (string.Equals(tr, text, StringComparison.Ordinal))
                {
                    // 模型回显原文（未真正翻译）：只复用"另一处已有的真译文"（≠源且非空）避免污染；
                    // 否则清空待补——绝不把中文原文当译文残留在文件里
                    var ex = existingAll.TryGetValue(text, out var ev2)
                             && !string.IsNullOrWhiteSpace(ev2)
                             && !string.Equals(ev2, text, StringComparison.Ordinal) ? ev2 : "";
                    value = ex;
                    if (string.IsNullOrWhiteSpace(ex)) result.Failed++;
                    else result.Translated++;
                }
                else
                {
                    value = tr;
                    result.Translated++;
                }

                // 广播到所有需要该文本的文件
                foreach (var file in textToFiles[text])
                    perFile[file].Merged[text] = value;

                result.Added++;
                progress?.Report(new TranslationProgress(k + 1, pendingTexts.Count, ""));
            }
        }

        // 每文件构建 FileEdit（仅在确有变更时）
        var edits = new List<FileEdit>();
        var preview = new StringBuilder();
        foreach (var file in targetFiles)
        {
            var pf = perFile[file];
            var newJson = RestoreSurrogatePairs(JsonSerializer.Serialize(pf.Merged, s_translationTypeInfo));
            var edit = _fileEditor.Build(file, pf.OldContent, newJson);
            if (!edit.IsNoop)
            {
                edits.Add(edit);
                preview.Append("────────────\n");
                preview.Append(_fileEditor.RenderDiff(edit));
            }
        }

        // 已删除/扫描不到的键（UI/系统文本保留，仅统计）
        var scannedSet = new HashSet<string>(scanned.Select(s => s.Text), StringComparer.Ordinal);
        result.Removed = existingAll.Count(k => !scannedSet.Contains(k.Key));

        result.PendingEdits = edits;
        result.PreviewText = preview.ToString();
        return result;
    }

    // ===== 已有翻译读取 =====

    private static Dictionary<string, string> LoadExistingAcrossLang(string langDir, string lang)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var dir = Path.Combine(langDir, lang);
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                MergeFile(file, dict);
        }
        else
        {
            var single = Path.Combine(langDir, lang + ".json");
            if (File.Exists(single))
                MergeFile(single, dict);
        }

        // 兼容旧 SDK 误写位置 <项目根>/Lang/{lang}（仅读，不写入）
        var projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(langDir));
        if (!string.IsNullOrEmpty(projectRoot))
        {
            var legacy = Path.Combine(projectRoot, ProjectConstants.LangDir, lang);
            if (Directory.Exists(legacy))
                foreach (var file in Directory.GetFiles(legacy, "*.json", SearchOption.AllDirectories))
                    MergeFile(file, dict);
        }

        return dict;
    }

    private static void MergeFile(string path, Dictionary<string, string> dict)
    {
        try
        {
            var parsed = ReadDict(File.ReadAllText(path, Encoding.UTF8));
            foreach (var (key, value) in parsed)
                dict[key] = value;
        }
        catch
        {
            // 单个文件解析失败跳过，不阻塞
        }
    }

    private static Dictionary<string, string> ReadDict(string json)
        => JsonSerializer.Deserialize(json, SdkJsonContext.Default.DictionaryStringString) ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, bool> GetTranslationStatus(string projectDir, string lang)
    {
        var existing = LoadExistingAcrossLang(LangDir(projectDir), lang);
        var status = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var text in ScanTranslatableTexts(projectDir).Select(s => s.Text).Distinct(StringComparer.Ordinal))
        {
            status[text] = IsTranslated(existing, text);
        }
        return status;
    }

    /// <summary>判定一条文本是否已翻译：译文存在、非空且不等于原文本身（空串/原文占位都不算已翻译）。</summary>
    private static bool IsTranslated(Dictionary<string, string> dict, string text)
        => dict.TryGetValue(text, out var translation)
           && !string.IsNullOrWhiteSpace(translation)
           && !string.Equals(translation, text, StringComparison.Ordinal);

    private static bool IsBuildArtifact(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("GeneratedKeys.cs", StringComparison.OrdinalIgnoreCase);
    }
}