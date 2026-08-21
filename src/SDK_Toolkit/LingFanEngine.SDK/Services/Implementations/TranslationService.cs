using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
/// <para>扫描 <c>Resources/Stories/**/*.story</c>（DSL）+ <c>src/**/*.cs</c>（C# API 文本）全部可翻译文本，
/// 增量维护 <c>Lang/{lang}/main.json</c>（与引擎 I18nService 目录加载约定一致），
/// 支持 Manual/AI/API 三种模式自动翻译。</para>
/// </summary>
public class TranslationService : ITranslationService
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ScanTranslatableTextsAsync(string projectDir)
        => Task.Run(() => ScanTranslatableTexts(projectDir));

    /// <inheritdoc/>
    public Task<TranslationSyncResult> SyncAsync(string projectDir, string lang)
        => Task.Run(() => Sync(projectDir, lang, new ManualTranslator(), null, false, "", CancellationToken.None));

    /// <inheritdoc/>
    public Task<TranslationSyncResult> SyncWithTranslatorAsync(
        string projectDir, string lang, ITranslator translator, string sourceLang = "", CancellationToken ct = default)
        => Task.Run(() => Sync(projectDir, lang, translator, null, false, sourceLang, ct), ct);

    /// <inheritdoc/>
    public Task<TranslationSyncResult> SyncWithTranslatorAsync(
        string projectDir, string lang, ITranslator translator,
        IProgress<TranslationProgress>? progress, string sourceLang = "", CancellationToken ct = default)
        => Task.Run(() => Sync(projectDir, lang, translator, progress, false, sourceLang, ct), ct);

    /// <inheritdoc/>
    public Task<TranslationSyncResult> SyncWithTranslatorAsync(
        string projectDir, string lang, ITranslator translator,
        bool forceRetranslate, string sourceLang = "", CancellationToken ct = default)
        => Task.Run(() => Sync(projectDir, lang, translator, null, forceRetranslate, sourceLang, ct), ct);

    /// <inheritdoc/>
    public Task<TranslationSyncResult> SyncWithTranslatorAsync(
        string projectDir, string lang, ITranslator translator,
        IProgress<TranslationProgress>? progress, bool forceRetranslate, string sourceLang = "", CancellationToken ct = default)
        => Task.Run(() => Sync(projectDir, lang, translator, progress, forceRetranslate, sourceLang, ct), ct);

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, bool>> GetTranslationStatusAsync(string projectDir, string lang)
        => Task.Run(() => GetTranslationStatus(projectDir, lang));

    // ===== 内部实现 =====

    private static string StoriesDir(string projectDir)
        => Path.Combine(projectDir, ProjectConstants.ResourcesDir, ProjectConstants.StoriesDir);

    // 与引擎运行时 I18nService（LangRoot = "Lang"）及 docs-site 多语言标准一致：
    // 语言根目录位于项目根 Lang/{lang}/（与 Stories/ 平级），而非 Resources/Lang/。
    private static string LangDir(string projectDir)
        => Path.Combine(projectDir, ProjectConstants.LangDir);

    private static string SrcDir(string projectDir)
        => Path.Combine(projectDir, "src");

    private static IReadOnlyList<string> ScanTranslatableTexts(string projectDir)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);

        // 1) DSL .story 扫描——兼容两种布局：
        //    模板：{projectDir}/Resources/Stories/**；引擎/Demo：{projectDir}/Stories/**
        //    兜底：项目根下任意 **/Stories/**.story（防目录结构差异/嵌套项目）
        var storyCount = 0;
        foreach (var candidate in new[]
                 {
                     StoriesDir(projectDir),                    // Resources/Stories
                     Path.Combine(projectDir, ProjectConstants.StoriesDir), // Stories
                 })
        {
            if (!Directory.Exists(candidate))
                continue;
            foreach (var file in Directory.GetFiles(candidate, "*.story", SearchOption.AllDirectories))
            {
                CollectStoryTexts(file, texts);
                storyCount++;
            }
        }

        // 兜底：标准布局未扫到任何 story 时，扫描项目根下所有 Stories 目录（兼容嵌套/异常结构）
        if (storyCount == 0 && Directory.Exists(projectDir))
        {
            foreach (var dir in Directory.GetDirectories(projectDir, "Stories", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(dir))
                    continue;
                foreach (var file in Directory.GetFiles(dir, "*.story", SearchOption.AllDirectories))
                {
                    CollectStoryTexts(file, texts);
                }
            }
        }

        // 2) C# 源码扫描——项目根下全部 .cs（排除 obj/bin/GeneratedKeys）
        // 兼容模板（根/src/**）与 Demo（src/Demo/**）两种布局
        if (Directory.Exists(projectDir))
        {
            foreach (var file in Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildArtifact(file))
                    continue;
                CollectCsTexts(file, texts);
            }
        }

        // 稳定排序——保证增量同步结果确定性
        return texts.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    /// <summary>排除构建产物目录（obj/bin）与自动生成密钥文件</summary>
    private static bool IsBuildArtifact(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("GeneratedKeys.cs", StringComparison.OrdinalIgnoreCase);
    }

    // ===== DSL 扫描 =====

    private static void CollectStoryTexts(string storyFile, HashSet<string> texts)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(storyFile);
        }
        catch (Exception)
        {
            return; // 单个文件读失败不阻塞整体扫描
        }

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
                case SayStmt say:
                    AddIfNonEmpty(texts, say.Text);
                    break;

                case MenuStmt menu:
                    AddIfNonEmpty(texts, menu.Prompt);
                    break;

                case MenuOptionStmt option:
                    AddIfNonEmpty(texts, option.Text);
                    break;

                case InputStmt input:
                    AddIfNonEmpty(texts, input.Prompt);
                    if (input.Options != null)
                        foreach (var opt in input.Options)
                            AddIfNonEmpty(texts, opt);
                    break;

                case NotifyStmt notify:
                    AddIfNonEmpty(texts, notify.Text);
                    break;

                case SaveStmt save:
                    AddIfNonEmpty(texts, save.Title);
                    break;

                // UI 元素（text/button/checkbox 等）——文本在 Properties["text"]
                case ShowElementStmt element:
                    CollectElementText(element.Element, texts);
                    break;
            }
        }
    }

    private static void CollectElementText(UiElementEntity element, HashSet<string> texts)
    {
        if (element == null)
            return;

        // 仅文本类元素（text/button/checkbox/dialog/narrator/speaker/choice）翻译；image/background 的 source 是资源路径不翻译
        var type = element.ElementType?.ToLowerInvariant() ?? "";
        if (type is "image" or "background" or "portrait" or "video" or "canvas" or "vbar" or "slider" or "panel" or "minigame" or "live2d")
            return;

        if (element.Properties != null && element.Properties.TryGetValue("text", out var text) && text != null)
            AddIfNonEmpty(texts, text.ToString());

        // 递归子元素（嵌套 Panel）
        if (element.Children != null)
        {
            foreach (var child in element.Children)
                CollectElementText(child, texts);
        }
    }

    // ===== C# 扫描 =====

    /// <summary>
    /// 扫描 C# 源码中的可翻译字符串字面量。
    /// <para>策略：剔除注释后提取所有含 CJK 的字符串字面量（普通 "..." / verbatim @"..." / 插值 $"..."/$@"..."）。
    /// 天然覆盖 SayAsync/ExtendAsync/ShowMenuAsync/ChoiceAsync/InputAsync/Notify/AddButton/AddMenu/AddText/SetScene 等全部 API 文本参数；
    /// 资源路径（无中文）与英文场景名不会误抓。</para>
    /// </summary>
    private static void CollectCsTexts(string csFile, HashSet<string> texts)
    {
        string source;
        try
        {
            source = File.ReadAllText(csFile);
        }
        catch (Exception)
        {
            return;
        }

        foreach (var literal in ExtractCSharpStringLiterals(source))
        {
            // 含 CJK 才纳入——灵泛引擎母语为中文
            if (ContainsCjk(literal))
                AddIfNonEmpty(texts, literal);
        }
    }

    private static bool ContainsCjk(string s)
    {
        foreach (var ch in s)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF) return true;      // 基本区
            if (ch >= 0x3400 && ch <= 0x4DBF) return true;      // 扩展 A
            if (ch >= 0xF900 && ch <= 0xFAFF) return true;      // 兼容区
        }
        return false;
    }

    /// <summary>提取 C# 源码中的字符串字面量（去注释、处理转义、支持 verbatim/插值）。</summary>
    public static IEnumerable<string> ExtractCSharpStringLiterals(string source)
    {
        var results = new List<string>();
        var sb = new StringBuilder();
        var i = 0;
        var n = source.Length;
        var inString = false;
        var isVerbatim = false;
        var isInterpolated = false;
        var braceDepth = 0; // 插值 { 深度
        var discard = false; // 插值表达式 → 运行时求值，静态文本不可确定 → 丢弃

        while (i < n)
        {
            var c = source[i];

            // 块注释 /* */ 或 // 行注释（仅代码状态）
            if (!inString && c == '/' && i + 1 < n)
            {
                if (source[i + 1] == '/')
                {
                    i += 2;
                    while (i < n && source[i] != '\n') i++;
                    continue;
                }
                if (source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    i += 2;
                    continue;
                }
            }

            // 字符串入口（代码状态）
            if (!inString)
            {
                // $@"..." 或 @$"..."（verbatim 插值）
                if (c == '@' && i + 1 < n && source[i + 1] == '"')
                {
                    inString = true; isVerbatim = true; isInterpolated = false;
                    i += 2;
                    continue;
                }
                if (c == '$' && i + 1 < n && source[i + 1] == '@' && i + 2 < n && source[i + 2] == '"')
                {
                    inString = true; isVerbatim = true; isInterpolated = true;
                    i += 3;
                    continue;
                }
                if (c == '@' && i + 1 < n && source[i + 1] == '$' && i + 2 < n && source[i + 2] == '"')
                {
                    inString = true; isVerbatim = true; isInterpolated = true;
                    i += 3;
                    continue;
                }
                // $"..."（插值）
                if (c == '$' && i + 1 < n && source[i + 1] == '"')
                {
                    inString = true; isVerbatim = false; isInterpolated = true;
                    i += 2;
                    continue;
                }
                // "..."（普通）
                if (c == '"')
                {
                    inString = true; isVerbatim = false; isInterpolated = false;
                    i += 1;
                    continue;
                }
                i++;
                continue;
            }

            // 字符串内
            if (isInterpolated && braceDepth == 0 && c == '{')
            {
                // 检查是否 {{（转义花括号 → 输出字面 {，可翻译）
                if (i + 1 < n && source[i + 1] == '{')
                {
                    sb.Append(c);
                    i += 2;
                    continue;
                }
                // 单 { = 插值表达式，运行时求值——文本不可静态确定，标记丢弃
                braceDepth = 1;
                discard = true;
                i++;
                continue;
            }
            if (isInterpolated && braceDepth > 0)
            {
                if (c == '{') { braceDepth++; i++; continue; }
                if (c == '}') { braceDepth--; i++; continue; }
                // 表达式内字符串（嵌套 "..."）——保持简单：直接跳过内容
                if (c == '"')
                {
                    i++;
                    while (i < n && source[i] != '"')
                    {
                        if (source[i] == '\\') i++;
                        i++;
                    }
                    i++;
                    continue;
                }
                i++;
                continue;
            }
            if (isVerbatim && c == '"')
            {
                if (i + 1 < n && source[i + 1] == '"') // "" 转义
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }
                // 结束
                Flush();
                inString = false; isVerbatim = false; isInterpolated = false; discard = false;
                i++;
                continue;
            }
            if (!isVerbatim && c == '\\' && i + 1 < n && !isInterpolated)
            {
                // 普通字符串转义（插值简化：不处理 \ 转义）
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

        // 文件末尾若仍在字符串中，强制收尾
        if (inString)
            Flush();

        return results;

        void Flush()
        {
            if (sb.Length > 0 && !discard)
                results.Add(sb.ToString());
            sb.Clear();
        }
    }

    // ===== 同步 =====

    private static void AddIfNonEmpty(HashSet<string> texts, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        texts.Add(text);
    }

    private static TranslationSyncResult Sync(
        string projectDir, string lang, ITranslator translator,
        IProgress<TranslationProgress>? progress, bool forceRetranslate, string sourceLang, CancellationToken ct)
    {
        var result = new TranslationSyncResult();
        var scanned = ScanTranslatableTexts(projectDir);
        result.Scanned = scanned.Count;

        var langDir = LangDir(projectDir);
        var outputPath = Path.Combine(langDir, lang, "main.json");

        // 读取已有翻译（目录形式 main.json 优先，降级单文件 Lang/{lang}.json）
        // 注意：翻译文件可能含 UI/系统文本（C# 侧，不在 .story 中）——必须全部保留，不能丢弃。
        var existing = LoadExistingTranslations(projectDir, lang);
        var scannedSet = new HashSet<string>(scanned, StringComparer.Ordinal);

        // 合并策略：从现有全量开始，为扫描到的新文本补占位/翻译，绝不删除已有键
        var merged = new Dictionary<string, string>(existing, StringComparer.Ordinal);

        // 先收集新增/待重翻条目，批量翻译一次（AI/API 核心优化：单请求多条的 30× 成本摊薄）
        // 默认跳过已翻译（值≠原文）；forceRetranslate=true 时忽略已有译文，全部重翻
        var pending = new List<string>();
        foreach (var text in scanned)
        {
            if (ct.IsCancellationRequested)
                break;

            var alreadyTranslated = existing.TryGetValue(text, out var translated)
                && !string.Equals(translated, text, StringComparison.Ordinal);

            if (!forceRetranslate && alreadyTranslated)
            {
                result.Kept++;
                continue;
            }
            pending.Add(text);
        }

        var done = 0;
        if (pending.Count > 0)
        {
            var translations = translator.TranslateBatchAsync(pending, lang, sourceLang, ct).GetAwaiter().GetResult();
            for (var i = 0; i < pending.Count; i++)
            {
                if (ct.IsCancellationRequested)
                    break;

                var text = pending[i];
                var translation = translations.Count > i ? translations[i] : null;
                if (string.IsNullOrWhiteSpace(translation))
                {
                    // 翻译失败：优先保留已有译文（若有），否则回退原文占位
                    if (existing.TryGetValue(text, out var existingVal) && !string.IsNullOrWhiteSpace(existingVal))
                        merged[text] = existingVal;
                    else
                        merged[text] = text;
                    result.Failed++;
                }
                else
                {
                    merged[text] = translation;
                    result.Translated++;
                }
                result.Added++;
                done++;
                progress?.Report(new TranslationProgress(done, pending.Count, text));
            }
        }

        // 报告：现有翻译中扫描不到的键数（UI 文本或已删除的旧 story 文本，仅统计不删除）
        result.Removed = existing.Count(k => !scannedSet.Contains(k.Key));

        // 写回（目录形式：Lang/{lang}/main.json）
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var json = JsonSerializer.Serialize(merged,
            LingFanEngine.SDK.Utils.SdkJsonContext.Default.DictionaryStringString);
        File.WriteAllText(outputPath, json, System.Text.Encoding.UTF8);

        result.OutputPath = outputPath;
        return result;
    }

    private static Dictionary<string, string> LoadExistingTranslations(string projectDir, string lang)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        var langDir = LangDir(projectDir); // 项目根 Lang/

        // 目录形式：Lang/{lang}/main.json 优先，其余文件递归合并（与 I18nService 顺序一致）
        var dir = Path.Combine(langDir, lang);
        if (Directory.Exists(dir))
        {
            var mainFile = Path.Combine(dir, "main.json");
            if (File.Exists(mainFile))
                MergeFile(dict, mainFile);

            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(file, mainFile, StringComparison.OrdinalIgnoreCase))
                    MergeFile(dict, file);
            }
        }
        else
        {
            // 降级：单文件 Lang/{lang}.json
            var singleFile = Path.Combine(langDir, $"{lang}.json");
            if (File.Exists(singleFile))
                MergeFile(dict, singleFile);
        }

        // 兼容旧版误写位置 Resources/Lang/{lang}：合并读取，避免已有人工/机器译文被忽略
        var legacyDir = Path.Combine(projectDir, ProjectConstants.ResourcesDir, ProjectConstants.LangDir, lang);
        if (Directory.Exists(legacyDir))
        {
            foreach (var file in Directory.GetFiles(legacyDir, "*.json", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                MergeFile(dict, file);
            }
        }

        return dict;
    }

    private static void MergeFile(Dictionary<string, string> dict, string path)
    {
        try
        {
            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize(json,
                LingFanEngine.SDK.Utils.SdkJsonContext.Default.DictionaryStringString);
            if (parsed == null)
                return;
            foreach (var (key, value) in parsed)
                dict[key] = value;
        }
        catch (Exception)
        {
            // 单个文件解析失败跳过，不阻塞
        }
    }

    private static IReadOnlyDictionary<string, bool> GetTranslationStatus(string projectDir, string lang)
    {
        var existing = LoadExistingTranslations(projectDir, lang);
        var scanned = ScanTranslatableTexts(projectDir);

        var status = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var text in scanned)
        {
            var translated = existing.TryGetValue(text, out var v) && !string.Equals(v, text, StringComparison.Ordinal);
            status[text] = translated;
        }
        return status;
    }
}
