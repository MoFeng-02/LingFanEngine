using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.DslCore;
using LingFanEngine.Dsl.ProjectIndex;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// <see cref="IDslLanguageService"/> 的引擎侧实现——只依赖 DslCore，不引用任何 UI 框架。
/// <para>规划 §2.1/§3：承载 tokenizer / 语义高亮 / 补全 / 悬浮 / 跳转 / 查找引用 / 诊断，
/// 并维护跨文件 <see cref="DslSymbolIndex"/>；EngineCore 不引用本工程，依赖单向 DslCore ← LanguageService ← SDK。</para>
/// </summary>
public sealed class DslLanguageService : IDslLanguageService
{
    // 路径键一律 OrdinalIgnoreCase（Windows 路径语义）：URI 还原的小写盘符与本地扫描的原样大小写
    // 必须命中同一条目，否则 didChange 与读请求分裂在两个键上（读到旧内容）。
    private readonly ConcurrentDictionary<string, DslDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private DslSymbolIndex _symbolIndex = new();
    private readonly IProjectIndex _projectIndex;
    /// <summary>每文件「逐行包围块关键字」缓存——供补全的块级上下文感知（scene 块内首词优先 UI 元素）。
    /// 在 UpdateDocument/IndexProject 重索引后同步重算；典型 .story 文件很小，开销可忽略。</summary>
    private readonly ConcurrentDictionary<string, string?[]> _enclosingBlocks = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>折叠结构缓存（区段 + 逐行深度），按「文档实例 + 内容版本号」O(1) 判失效（编辑自增 <see cref="DslDocument.Version"/> → 自动失效）。
    /// 后台 <see cref="IndexProject"/> 建索引后预热，使首次 <c>foldingRange</c> 请求直接命中 → 瞬时（<see cref="ComputeStructure"/> 改 span 零分配 + 缓存命中，大文件不再每次重算 + Split 逐行分配）。</summary>
    private readonly ConcurrentDictionary<string, FoldingCacheEntry> _foldingCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>语义 token 缓存，按「文档实例 + 内容版本号 + 当前符号索引实例」O(1) 判失效（符号索引交换后旧条目自动失效）。
    /// 同样在 <see cref="IndexProject"/> 预热，使首次 <c>semanticTokens/full</c> 直接命中。</summary>
    private readonly ConcurrentDictionary<string, SemanticCacheEntry> _semanticCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>折叠缓存条目：文档实例 + 内容版本号 + 折叠区段 + 逐行嵌套深度（O(1) 失效判等，无 O(n) 全文比对）。</summary>
    private sealed record FoldingCacheEntry(DslDocument? Doc, int Version, List<(int Start, int End)> Foldings, int[] Depths);
    /// <summary>语义缓存条目：文档实例 + 内容版本号 + 计算时所用的符号索引实例 + token 序列。</summary>
    private sealed record SemanticCacheEntry(DslDocument? Doc, int Version, DslSymbolIndex UsedIndex, IReadOnlyList<SemanticToken> Tokens);
    /// <summary>资源联合索引是否已建立（ScanProject 成功）。未建立前不跑资源/命令诊断，避免「空索引 → 全盘误报未找到资源」。</summary>
    private bool _scanned;

    public DslLanguageService(IProjectIndex? projectIndex = null)
    {
        _projectIndex = projectIndex ?? new LingFanEngine.Dsl.ProjectIndex.ProjectIndex();
    }

    public void UpdateDocument(string filePath, string text, DirtyRange? dirty = null)
    {
        if (_documents.TryGetValue(filePath, out var doc))
        {
            var result = doc.Update(text, dirty);
            if (result.Incremental && result.AffectedLines is { } lines)
                _symbolIndex.IndexFileIncremental(filePath, lines, doc.Source, result.AffectedStartOld, result.OldAffectedEnd, result.Delta);
            else
                _symbolIndex.IndexFile(filePath, doc.GetAllTokens(), doc.Source);
        }
        else
        {
            doc = new DslDocument(filePath, text);
            _documents[filePath] = doc;
            _symbolIndex.IndexFile(filePath, doc.GetAllTokens(), doc.Source);
        }
        RecomputeEnclosing(filePath, text);
    }

    /// <summary>定向刷新用：快照某文件当前定义的符号键（委托 <see cref="DslSymbolIndex.SnapshotDefinitions"/>）。</summary>
    public HashSet<SymbolKey> SnapshotDefinitions(string path) => _symbolIndex.SnapshotDefinitions(path);

    /// <summary>定向刷新用：由定义前后快照求受影响的文件集合（委托 <see cref="DslSymbolIndex.GetAffectedFiles"/>）。</summary>
    public HashSet<string> GetAffectedFilesByDefinitionChange(string path, HashSet<SymbolKey> before)
        => _symbolIndex.GetAffectedFiles(path, before);

    public void RemoveDocument(string filePath)
    {
        _documents.TryRemove(filePath, out _);
        _symbolIndex.RemoveFile(filePath);
        _enclosingBlocks.TryRemove(filePath, out _);
        // 同路径重建的文档是新实例（Version 重置为 1），实例守卫已保证不误命中；
        // 此处显式清除避免长驻 LSP 下缓存条目无限堆积。
        _foldingCache.TryRemove(filePath, out _);
        _semanticCache.TryRemove(filePath, out _);
    }

    /// <summary>
    /// 跨文件符号索引（项目级 .story 全量重建）。为避免阻塞 LSP 读请求（folding/semantic/diagnostics），
    /// 本方法在「后台构建」模式下被调用：它先在一份<b>全新</b>的 <see cref="DslSymbolIndex"/> 上完成全部索引，
    /// 末了才将字段引用一次性替换为新实例——读请求要么看到旧实例、要么看到新实例，绝不会读到半构建状态（引用赋值为原子操作）。
    /// <para>兜底合并：构建期间可能有新 didOpen 的文档写入 <see cref="_documents"/>；末尾再扫一遍，把构建期新增的文档补编入新索引，
    /// 防止后台索引覆盖丢失其符号（自 opened 文档的增量更新在下次 didChange 自愈，此处只保证初次符号可用）。</para>
    /// </summary>
    public void IndexProject(IReadOnlyList<(string Path, string Text)> files)
    {
        var indexed = new HashSet<string>(StringComparer.Ordinal);
        var built = new DslSymbolIndex();

        // 1) 当前已打开文档（此刻快照）一并编入，保证 didOpen 早于本调用的文件符号不丢
        foreach (var kv in _documents)
        {
            indexed.Add(kv.Key);
            built.IndexFile(kv.Key, kv.Value.GetAllTokens(), kv.Value.Source);
        }
        // 2) 项目级 .story 文件
        foreach (var (path, text) in files)
        {
            indexed.Add(path);
            var doc = new DslDocument(path, text);
            _documents[path] = doc;
            built.IndexFile(path, doc.GetAllTokens(), doc.Source);
            RecomputeEnclosing(path, text);
        }
        // 3) 兜底：合并构建期间新 didOpen 的文档（避免被后台索引覆盖丢失）
        foreach (var kv in _documents)
            if (indexed.Add(kv.Key))
                built.IndexFile(kv.Key, kv.Value.GetAllTokens(), kv.Value.Source);

        _symbolIndex = built;

        // 预热折叠/语义缓存：此刻符号索引已就绪，统一为所有已加载文档算好并缓存，
        // 用户首次 foldingRange/semanticTokens 请求直接命中 → 大文件也瞬时（不再现场重算 + 逐 token 线性扫描）。
        foreach (var kv in _documents)
        {
            GetFoldingRegions(kv.Key);
            GetSemanticTokens(kv.Key);
        }
    }

    /// <summary>
    /// 扫描项目根，建联合资源索引（图片/音频/视频/字体等），供资源路径补全/悬停/跳转使用。
    /// 与 DSL 符号索引（<see cref="_symbolIndex"/>）解耦——资源来自磁盘文件树，符号来自 *.story 解析。
    /// </summary>
    public void ScanProject(string rootPath)
    {
        _projectIndex.Scan(rootPath);
        _scanned = true;
    }

    /// <summary>资源文件变更（didChangeWatchedFiles 增量维护）。</summary>
    public void UpdateResource(string absolutePath) => _projectIndex.UpdateResource(absolutePath);

    /// <summary>资源文件删除（didChangeWatchedFiles 增量维护）。</summary>
    public void RemoveResource(string absolutePath) => _projectIndex.RemoveResource(absolutePath);

    public IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return System.Array.Empty<SemanticToken>();
        // 命中缓存：同一文档实例 + 内容版本未变 + 符号索引实例未变（索引交换后旧条目自动失效）→ 直接返回，避免大文件逐 token 线性扫描。
        if (_semanticCache.TryGetValue(filePath, out var cached) && cached.Doc == doc && cached.Version == doc.Version && ReferenceEquals(cached.UsedIndex, _symbolIndex))
            return cached.Tokens;
        var tokens = doc.GetAllTokens();
        var source = doc.Source;
        var result = new SemanticToken[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            var cat = DslSemanticClassifier.Classify(tokens[i], source);
            var occ = _symbolIndex.FindOccurrenceAt(filePath, tokens[i].Offset);
            if (occ != null)
                cat = occ.Value.Role == SymbolRole.Definition
                    ? SemanticCategory.SymbolDefinition
                    : SemanticCategory.SymbolReference;
            else if (tokens[i].Kind == DslTokenKind.String && RefOfStringToken(doc, tokens[i], source) == DslCompletionRef.Resource)
                // 资源路径引用（image src= / bgm / sprite src= / live2d_char src= 等）：区别于普通 String，着为 Resource 并可跳转。
                cat = SemanticCategory.Resource;
            result[i] = new SemanticToken(tokens[i].Offset, tokens[i].Length, cat);
        }
        _semanticCache[filePath] = new SemanticCacheEntry(doc, doc.Version, _symbolIndex, result);
        return result;
    }

    /// <summary>
    /// 上下文感知补全——驱动源为 DslCore 的 <see cref="DslGrammar"/>（与 <see cref="DslStatementParser"/> 同源单一真相源），
    /// 并用 <see cref="DslTokenizer"/> 解析光标所在行的真实 token 序列判定上下文，取代旧字符串切片启发式。
    /// <list type="bullet">
    ///   <item><description>行首→语句关键字 + UI 元素类型。</description></item>
    ///   <item><description>光标在引号字符串内→按语法槽位（位置参 / 位置词 / key=）给场景/标签/函数/角色/样式/过渡等引用。</description></item>
    ///   <item><description>光标在 key= 值→按参数值种类给枚举 / 布尔 / 过渡 / 缓动。</description></item>
    ///   <item><description>光标在 { 表达式} 内→变量名（跨文件索引）。</description></item>
    ///   <item><description>call/jump/label 等裸标识符目标→标签/函数名。</description></item>
    /// </list>
    /// 全部引用候选取自跨文件符号索引 <see cref="_symbolIndex"/>，故所见即项目真实定义。
    /// </summary>
    public IReadOnlyList<CompletionItem> GetCompletion(string filePath, int offset)
    {
        var items = new List<CompletionItem>();
        if (!_documents.TryGetValue(filePath, out var doc)) return items;
        var source = doc.Source;
        var line = doc.GetLineIndex(offset);
        var lineStart = doc.GetLineStart(line);
        // 块级上下文：光标所在行的「包围块」（scene/if/while/for/func/switch/foreach），取自逐行缓存。
        var enclosing = _enclosingBlocks.TryGetValue(filePath, out var eb) && line < eb.Length ? eb[line] : null;

        // 当前行文本（含换行前）
        var lineLen = source.Length - lineStart;
        if (lineLen > 0)
        {
            var nl = source.Slice(lineStart).IndexOf('\n');
            if (nl >= 0) lineLen = nl;
        }
        var lineText = source.Slice(lineStart, lineLen);
        var lineTokens = DslTokenizer.TokenizeLine(lineText, lineStart);

        var (ctx, spec, prefix, replaceStart) = ResolveContext(lineTokens, source, offset);
        switch (ctx)
        {
            case CompletionContext.StatementStart:
                if (enclosing == "scene")
                {
                    // scene 块内：首词大概是 UI 元素类型（text/button/image/...）——优先列出，再补场景块内常见语句。
                    foreach (var kw in DslKeywords.UiElementTypes)
                        items.Add(KwWithCategory(kw, "element", "UI 元素"));
                    foreach (var kw in s_sceneBlockStatements)
                    {
                        var cat = GetStatementCategory(kw);
                        items.Add(KwWithCategory(kw, "statement", cat));
                    }
                }
                else
                {
                    foreach (var kw in DslKeywords.Statements)
                    {
                        var cat = GetStatementCategory(kw);
                        items.Add(KwWithCategory(kw, "statement", cat));
                    }
                    foreach (var kw in DslKeywords.UiElementTypes)
                        items.Add(KwWithCategory(kw, "element", "UI 元素"));
                }
                break;

            case CompletionContext.ParameterName:
                if (spec != null)
                {
                    // 收集当前行已使用的参数名（key=），补全时排除，避免重复提示
                    var usedParams = CollectUsedParamNames(lineTokens, source, spec);
                    foreach (var kv in spec.NamedParams)
                    {
                        if (usedParams.Contains(kv.Key)) continue;  // 已使用，跳过
                        var cat = GetParameterCategory(kv.Key);
                        // 参数名补全 InsertText 自动追加 '='，选中后直接进入值输入
                        var item = KwWithCategory(kv.Key, "parameter", cat);
                        item.InsertText = kv.Key + "=";
                        items.Add(item);
                    }
                }
                break;

            case CompletionContext.VariableReference:
                var seenVars = new HashSet<string>(StringComparer.Ordinal);
                // 仅取本文件的变量作用域：let/local 是文件级局部，不跨文件补全（fileB 不应提示 fileA 的 let）。
                foreach (var (n, scopeInfo) in _symbolIndex.GetVariablesWithScope(filePath))
                {
                    seenVars.Add(n);
                    items.Add(new CompletionItem(n, n, "variable", ScopeBadge(scopeInfo)));
                }
                // M8：C# 侧状态键（state.Set/Get("key")）——DSL {var} 亦可引用，双向可见。
                foreach (var n in _projectIndex.GetCsVariableKeys())
                    if (seenVars.Add(n)) items.Add(new CompletionItem(n, n, "variable", "C# 状态键"));
                // 表达式内置函数（random/min/max/abs/clamp）——{…} 表达式上下文直接可用。
                foreach (var (fn, sig) in DslKeywordDocs.BuiltinFunctions)
                    items.Add(new CompletionItem(fn, fn, "function", sig));
                // 行内富文本标记（{b}{i}{u}{w}{fast}{p}{color=}{font=}{size=} 等）与变量同为 {…} 内合法内容。
                foreach (var tag in DslInlineTags.AllTags)
                    items.Add(new CompletionItem(tag, tag, "tag", "行内标记"));
                break;

            case CompletionContext.SceneName:
                // navigate 目标可以是 scene 或 label；scene 定义
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Scene))
                    items.Add(new CompletionItem($"\"{n}\"", n, "scene"));
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Label))
                    items.Add(new CompletionItem(n, n, "label"));
                // M8：C# 侧场景导航目标（Navigate("x") 等）
                foreach (var n in _projectIndex.GetSceneTargets())
                    items.Add(new CompletionItem($"\"{n}\"", n, "scene", "C# 场景"));
                break;

            case CompletionContext.LabelName:
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Label))
                    items.Add(new CompletionItem(n, n, "label"));
                break;

            case CompletionContext.FuncName:
                // call 目标可以是 func 或 label（示例多用 label 做子过程）
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Func))
                    items.Add(new CompletionItem(n, n, "func"));
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Label))
                    items.Add(new CompletionItem(n, n, "label"));
                break;

            case CompletionContext.SpeakerName:
            case CompletionContext.CharacterName:
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Character))
                    items.Add(new CompletionItem($"\"{n}\"", n, "character"));
                break;

            case CompletionContext.StyleName:
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Style))
                    items.Add(new CompletionItem(n, n, "style"));
                break;

            case CompletionContext.EnumValue:
                foreach (var v in s_sceneTypes) items.Add(new CompletionItem(v, v, "enum"));
                break;

            case CompletionContext.BooleanValue:
                items.Add(new CompletionItem("true", "true", "enum"));
                items.Add(new CompletionItem("false", "false", "enum"));
                break;

            case CompletionContext.TrueOnlyValue:
                items.Add(new CompletionItem("true", "true", "enum"));
                break;

            case CompletionContext.TransitionValue:
                foreach (var v in DslTransitionNames.All) items.Add(new CompletionItem(v, v, "enum"));
                break;

            case CompletionContext.EasingValue:
                foreach (var v in DslEasingNames.All) items.Add(new CompletionItem(v, v, "enum"));
                break;

            case CompletionContext.ResourceValue:
                // 资源路径补全——取自项目联合索引（前缀匹配相对路径）。限流避免前缀为空时整树刷屏（客户端仍按已输前缀模糊过滤）。
                var cap = 0;
                foreach (var r in _projectIndex.GetResourceCandidates(prefix))
                {
                    if (cap++ >= 300) break;
                    items.Add(new CompletionItem(r.RelativePath, r.RelativePath, "resource", $"{r.Kind} · {r.FormattedSize}"));
                }
                break;

            case CompletionContext.CommandValue:
                // 按钮命令名——取自 C# 命令注册表（M8 跨语言索引；未实现前为空，不影响其它补全）。按已输前缀过滤。
                foreach (var c in _projectIndex.GetCommandNames())
                    if (c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        items.Add(new CompletionItem(c, c, "command", "命令 (C# 注册)"));
                break;

            case CompletionContext.AlignValue:
                // 对齐枚举值（与 ControlFactory 解析一致）。
                foreach (var v in s_alignValues) items.Add(new CompletionItem(v, v, "enum"));
                break;

            case CompletionContext.None:
            default:
                // 自由文本 / 数字 / 对话正文等无补全上下文：不弹，避免干扰输入。
                break;
        }

        // 含分隔符的候选（资源路径含 /、命令名含 _）：用精确替换起点覆盖调用方的词边界探测，
        // 否则会把已输入前缀重复拼回（"Audio/cri" 选 "Audio/x.mp3" → "Audio/Audio/x.mp3"）。
        if (replaceStart >= 0)
            foreach (var it in items) it.ReplaceStart = replaceStart;

        // ── 智能补全增强：sortText / filterText / preselect / documentation / commitCharacters ──
        EnrichCompletionItems(items, prefix, ctx);

        return items;
    }

    private static readonly string[] s_sceneTypes = { "game", "menu", "ui" };

    /// <summary>
    /// 补全后处理——填充 sortText / filterText / preselect / documentation / commitCharacters。
    /// <para>排序策略（对标 C# IntelliSense）：
    ///   ① 已定义符号（定义站点）最前；
    ///   ② 前缀匹配优先（区分大小写后退到不区分大小写）；
    ///   ③ 同类内按字母序。</para>
    /// </summary>
    private static void EnrichCompletionItems(List<CompletionItem> items, string prefix, CompletionContext ctx)
    {
        if (items.Count == 0) return;
        var hasPrefix = !string.IsNullOrEmpty(prefix);
        var bestScore = int.MinValue;
        CompletionItem? bestItem = null;

        foreach (var it in items)
        {
            // ── filterText：客户端据此做过滤 ──
            it.FilterText = it.DisplayText;

            // ── sortText：智能排序 ──
            var score = 0;
            var label = it.DisplayText;
            if (hasPrefix)
            {
                // 精确前缀匹配（大小写敏感）得分最高
                if (label.StartsWith(prefix, StringComparison.Ordinal))
                    score = 10000 - label.Length; // 同前缀越短越靠前
                else if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    score = 9000 - label.Length;   // 不区分大小写次之
                else if (label.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                    score = 8000 - label.Length;   // 子串匹配再次之
                else
                    score = 7000 - label.Length;   // 无匹配但保留
            }
            else
            {
                // 无前缀时：已定义符号 > 关键字 > 其他
                score = it.Kind switch
                {
                    "scene" or "label" or "func" or "character" or "style" or "variable" => 9500 - label.Length,
                    "element" => 9000 - label.Length,    // UI 元素类型
                    "statement" => 8500 - label.Length,   // 语句关键字
                    "parameter" => 8000 - label.Length,   // 参数名
                    "enum" => 7500 - label.Length,        // 枚举值
                    "tag" => 7000 - label.Length,         // 行内标记
                    "resource" => 6000 - label.Length,    // 资源路径
                    _ => 5000 - label.Length,
                };
            }
            it.SortText = score.ToString("D6");

            // ── preselect：最佳匹配预选 ──
            if (score > bestScore)
            {
                bestScore = score;
                bestItem = it;
            }

            // ── documentation：从 DslKeywordDocs 获取富文本文档 ──
            if (DslKeywordDocs.TryGet(it.DisplayText, out var kd))
            {
                var doc = $"**{kd.Summary}";
                if (kd.Usage != null) doc += $"\n\n```dsl\n{kd.Usage}\n```";
                it.Documentation = doc;
            }
            else if (it.Kind == "parameter")
            {
                // 参数名：显示所属分类
                var cat = GetParameterCategory(it.DisplayText);
                it.Documentation = $"**{cat}**\n\n键值参数，用法：`{it.DisplayText}=值`";
            }
            else if (it.Kind == "variable")
            {
                // 变量：显示作用域
                it.Documentation = $"**变量引用**\n\n作用域：{it.Detail ?? "全局"}";
            }
            else if (it.Kind == "element")
            {
                // UI 元素：显示元素说明
                it.Documentation = $"**UI 元素**\n\n在 scene 块内使用，定义界面组件。";
            }

            // ── commitCharacters：插入后自动提交的字符 ──
            it.CommitCharacters = it.Kind switch
            {
                // 参数名补全后自动加 = 号并触发值补全
                "parameter" => null, // 不自动提交，让用户输入 =
                // 关键字/语句补全后空格自动提交
                "statement" or "element" or "tag" => " ",
                // 枚举值/布尔值补全后空格自动提交
                "enum" => " ",
                // 标签/函数/场景名补全后不自动提交（可能要加引号或继续输入）
                _ => null,
            };
        }

        // 预选最佳匹配
        if (bestItem != null && hasPrefix)
            bestItem.Preselect = true;
    }

    /// <summary>对齐枚举值（与 ControlFactory 的 align/halign/valign 解析一致）。</summary>
    private static readonly string[] s_alignValues = { "left", "center", "right", "stretch", "top", "bottom" };

    /// <summary>scene 块内除 UI 元素外也可出现的语句关键字（补全候选补充，不覆盖 UI 元素优先）。</summary>
    private static readonly string[] s_sceneBlockStatements = { "navigate", "call_screen", "show", "hide", "nvl", "animate", "background", "character", "window", "style" };

    /// <summary>补全上下文（语法驱动，取代旧字符串切片启发式）。</summary>
    private enum CompletionContext
    {
        StatementStart, ParameterName, VariableReference, SceneName, LabelName,
        FuncName, SpeakerName, CharacterName, StyleName, EnumValue, BooleanValue,
        TrueOnlyValue, TransitionValue, EasingValue, ResourceValue, CommandValue,
        AlignValue, None,
    }

    /// <summary>带文档详情的补全项（Detail 取自 <see cref="DslKeywordDocs"/>）。</summary>
    private static CompletionItem Kw(string kw, string kind) =>
        DslKeywordDocs.TryGet(kw, out var d)
            ? new CompletionItem(kw, kw, kind, d.Summary)
            : new CompletionItem(kw, kw, kind);

    /// <summary>带分类前缀的补全项——区分 UI 元素/功能关键字/参数/变量等。</summary>
    private static CompletionItem KwWithCategory(string kw, string kind, string category, string? extraDetail = null)
    {
        var detail = extraDetail != null ? $"{category} · {extraDetail}" : category;
        if (DslKeywordDocs.TryGet(kw, out var d))
            detail += $"\n{d.Summary}";
        return new CompletionItem(kw, kw, kind, detail);
    }

    private static void AddKeywords(List<CompletionItem> items, IReadOnlySet<string> keywords, string kind)
    {
        foreach (var kw in keywords) items.Add(Kw(kw, kind));
    }

    /// <summary>获取语句关键字的分类标签（用于补全项 detail 前缀）。</summary>
    private static string GetStatementCategory(string kw)
    {
        if (DslKeywords.ControlFlow.Contains(kw)) return "控制流";
        if (DslKeywords.Navigation.Contains(kw)) return "导航";
        if (DslKeywords.DataOp.Contains(kw)) return "数据操作";
        if (DslKeywords.Media.Contains(kw)) return "媒体";
        if (DslKeywords.Display.Contains(kw)) return "显示/动画";
        if (DslKeywords.SaveLoad.Contains(kw)) return "存档系统";
        if (DslKeywords.Chapter.Contains(kw)) return "章节/成就";
        if (DslKeywords.Rollback.Contains(kw)) return "回溯控制";
        if (DslKeywords.Playback.Contains(kw)) return "播放控制";
        if (DslKeywords.TimeEvent.Contains(kw)) return "时间事件";
        if (DslKeywords.Notify.Contains(kw)) return "通知/调试";
        if (DslKeywords.UiEnhance.Contains(kw)) return "UI 增强";
        return "语句";
    }

    /// <summary>获取参数名的分类标签（用于补全项 detail 前缀）。</summary>
    private static readonly HashSet<string> _gridAttrs = new(StringComparer.Ordinal) { "col", "row", "colspan", "rowspan" };
    private static readonly HashSet<string> _layoutAttrs = new(StringComparer.Ordinal) { "x", "y", "xoffset", "yoffset", "xanchor", "yanchor", "margin", "padding", "right", "bottom", "minWidth", "minHeight", "maxWidth", "maxHeight" };
    private static readonly HashSet<string> _visualAttrs = new(StringComparer.Ordinal) { "opacity", "visible", "enabled", "zindex", "clipToBounds", "cursor" };
    private static readonly HashSet<string> _transformAttrs = new(StringComparer.Ordinal) { "rotation", "scale", "scaleX", "scaleY" };
    private static readonly HashSet<string> _borderAttrs = new(StringComparer.Ordinal) { "cornerRadius", "borderBrush", "borderColor", "borderThickness" };
    private static readonly HashSet<string> _containerAttrs = new(StringComparer.Ordinal) { "spacing", "direction", "columns", "rows" };

    private static string GetParameterCategory(string param)
    {
        if (DslKeywords.ElementAttributes.Contains(param))
        {
            if (_gridAttrs.Contains(param)) return "Grid 附着属性";
            if (_layoutAttrs.Contains(param)) return "布局参数";
            if (_visualAttrs.Contains(param)) return "外观属性";
            if (_transformAttrs.Contains(param)) return "变换属性";
            if (_borderAttrs.Contains(param)) return "边框属性";
            if (_containerAttrs.Contains(param)) return "容器属性";
            return "元素属性";
        }
        return "参数";
    }

    /// <summary>变量作用域徽标（B32 升级：含场景/标签级局部）。
    /// 入参为作用域信息串：<c>"全局"</c>(define/set) / <c>""</c>(文件局部) / <c>"scene/名"</c>(场景局部) / <c>"label/名"</c>(标签局部)。</summary>
    private static string ScopeBadge(string scopeInfo) =>
        scopeInfo == "全局" ? "全局"
        : scopeInfo.Length == 0 ? "文件局部"
        : scopeInfo.StartsWith("scene/", StringComparison.Ordinal) ? "场景局部"
        : scopeInfo.StartsWith("label/", StringComparison.Ordinal) ? "标签局部"
        : "局部";

    /// <summary>定义引用的回退种类：navigate 目标可以是 scene 或 label；jump/menu 目标可以是 label 或 scene；call 目标可以是 func 或 label。</summary>
    private static SymbolKind? FallbackKind(SymbolKind kind) => kind switch
    {
        SymbolKind.Label => SymbolKind.Scene,
        SymbolKind.Scene => SymbolKind.Label,
        SymbolKind.Func => SymbolKind.Label,
        SymbolKind.Command => null, // C# 侧定义，DSL 无对应定义站点
        _ => null,
    };

    /// <summary>变量出现的作用域徽标（悬浮信息用）：解析到该引用实际绑定的声明，按其声明作用域标注
    /// （let/local → 文件/场景/标签局部；define/set → 全局）。引用自身不携带生命周期级别，须查声明。</summary>
    private string VarScopeBadge(SymbolOccurrence o)
    {
        var def = _symbolIndex.Resolve(o.Kind, FallbackKind(o.Kind), o.Name, o.FilePath, o.ScopePath);
        if (def != null && def.Value.Scope == SymbolScope.Local)
            return ScopeBadge(def.Value.ScopePath);
        return "全局";
    }

    // ===== 语法驱动的补全上下文判定（取代 GetCompletionContext 字符串切片）=====

    /// <summary>
    /// 解析光标所在行的补全上下文。先判表达式插值（{ 未闭合）→ 变量；
    /// 再判光标是否在引号字符串内→按语法槽位取引用；否则按 token 序列判定行首/key=值/位置词/参数名/裸标识符目标。
    /// </summary>
    private static (CompletionContext Ctx, DslStmtGrammar? Spec, string Prefix, int ReplaceStart) ResolveContext(DslToken[] tokens, ReadOnlySpan<char> source, int offset)
    {
        if (tokens.Length == 0) return (CompletionContext.StatementStart, null, string.Empty, -1);

        // 1) 表达式插值上下文（{ ... 未闭合）→ 变量名。优先级最高（say "{x}" 既在字符串内又在插值内）。
        if (IsInInterpolation(source, offset))
            return (CompletionContext.VariableReference, null, string.Empty, -1);

        // 2) 光标位于引号字符串内部（open quote 之后）→ 按该字符串的语法槽位取引用
        for (var si = 0; si < tokens.Length; si++)
        {
            var t = tokens[si];
            if (t.Kind == DslTokenKind.String && offset > t.Offset && offset < t.Offset + t.Length)
            {
                // 光标在引号字符串内：提取已输入的相对路径前缀（开引号之后、光标之前），用于资源前缀匹配。
                var prefix = offset > t.Offset + 1
                    ? source.Slice(t.Offset + 1, offset - t.Offset - 1).ToString()
                    : string.Empty;
                var spec = DslGrammar.TryGet(tokens[0].GetText(source).ToString());
                // 2a) key= 值：字符串前最近一个紧邻标识符的 '='
                for (var i = si - 1; i >= 1; i--)
                {
                    if (tokens[i].Kind == DslTokenKind.Symbol && source[tokens[i].Offset] == '=')
                    {
                        var prev = tokens[i - 1];
                        if (prev.Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
                        {
                            var key = prev.GetText(source).ToString();
                            var r = spec?.NamedParams.TryGetValue(key, out var rv) == true ? rv : DslCompletionRef.None;
                            return (RefToContext(r), spec, prefix, t.Offset + 1);
                        }
                    }
                }
                // 2b) 位置词槽：字符串前紧邻的单词（by / with / scene ...）
                if (si - 1 >= 0)
                {
                    var prevText = tokens[si - 1].GetText(source).ToString();
                    if (spec?.PositionalWordRefs.TryGetValue(prevText, out var rw) == true)
                        return (RefToContext(rw), spec, prefix, t.Offset + 1);
                }
                // 2c) 首个位置参
                return (RefToContext(spec?.PositionalRef ?? DslCompletionRef.None), spec, prefix, t.Offset + 1);
            }
        }

        // 3) 非字符串、非插值：按 token 序列判定
        var first = tokens[0];
        var firstName = first.GetText(source).ToString();
        var spec2 = DslGrammar.TryGet(firstName);

        // 正在输入行首第一个词（光标仍在该词内，未带尾随空格）→ 语句/元素候选
        if (offset <= first.Offset + first.Length)
            return (CompletionContext.StatementStart, null, string.Empty, -1);

        // key= 值补全：光标前最近 '=' 紧邻一个标识符（参数名）。
        // 若光标已越过 = 右侧的值 token（如 button text="hello" |），
        // 说明该参数已输入完毕——跳过继续向上找更早的 '='，
        // 最终兜底到 ParameterName 补全剩余参数名。
        for (var i = tokens.Length - 1; i >= 1; i--)
        {
            if (tokens[i].Kind == DslTokenKind.Symbol && source[tokens[i].Offset] == '=')
            {
                var prev = tokens[i - 1];
                if (prev.Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
                {
                    // 值起点 = '=' 之后（如 cmd=open_sa → 替换 "open_sa"）
                    var valueStart = tokens[i].Offset + 1;
                    // 光标已越过 = 右侧的值 token → 该 key=value 已完成，跳过
                    if (i + 1 < tokens.Length && offset > tokens[i + 1].Offset + tokens[i + 1].Length)
                        continue;
                    var key = prev.GetText(source).ToString();
                    // 命中命名参数 → 取其值种类；否则视为表达式右值（set x = … / if a == … / while c && …）→ 变量名补全。
                    var r = spec2?.NamedParams.TryGetValue(key, out var rv) == true ? rv : DslCompletionRef.Expression;
                    return (RefToContext(r), spec2, string.Empty, valueStart);
                }
            }
        }

        // menu 选项目标："text" -> label
        for (var i = tokens.Length - 1; i >= 0; i--)
        {
            if (tokens[i].Kind == DslTokenKind.Symbol && IsArrow(tokens[i], source))
                return (CompletionContext.LabelName, spec2, string.Empty, -1);
        }

        // 关键字后第一个位置参 / 参数名：
        // 若位置参是「裸标识符」引用（过渡名 / 枚举 / 布尔 / 标签 / 函数 / 表达式），优先给对应值候选——
        // 否则若该语句有命名参数或属 UI 元素，给参数名候选。
        // 引号类引用（资源 / 场景 / 角色 / 样式 / 说话人）不在此处裸提示（会空插无引号裸路径导致解析错误），
        // 由字符串上下文（step 2）在用户键入 " 后给出。
        if (spec2 != null)
        {
            var bare = spec2.PositionalRef is DslCompletionRef.Label or DslCompletionRef.Func or DslCompletionRef.LabelOrFunc
                                          or DslCompletionRef.Transition or DslCompletionRef.Easing or DslCompletionRef.SceneType
                                          or DslCompletionRef.Boolean or DslCompletionRef.TrueOnly or DslCompletionRef.Expression;
            if (bare)
                return (RefToContext(spec2.PositionalRef), spec2, string.Empty, -1);
            if (spec2.NamedParams.Count > 0 || spec2.IsUiElement)
                return (CompletionContext.ParameterName, spec2, string.Empty, -1);
        }

        return (CompletionContext.None, spec2, string.Empty, -1);
    }

    private static CompletionContext RefToContext(DslCompletionRef r) => r switch
    {
        DslCompletionRef.Scene => CompletionContext.SceneName,
        DslCompletionRef.Label => CompletionContext.LabelName,
        DslCompletionRef.Func => CompletionContext.FuncName,
        DslCompletionRef.LabelOrFunc => CompletionContext.FuncName,
        DslCompletionRef.Speaker => CompletionContext.SpeakerName,
        DslCompletionRef.Character => CompletionContext.CharacterName,
        DslCompletionRef.Style => CompletionContext.StyleName,
        DslCompletionRef.Transition => CompletionContext.TransitionValue,
        DslCompletionRef.Easing => CompletionContext.EasingValue,
        DslCompletionRef.SceneType => CompletionContext.EnumValue,
        DslCompletionRef.Boolean => CompletionContext.BooleanValue,
        DslCompletionRef.TrueOnly => CompletionContext.TrueOnlyValue,
        DslCompletionRef.Expression => CompletionContext.VariableReference,
        DslCompletionRef.Resource => CompletionContext.ResourceValue,
        DslCompletionRef.Command => CompletionContext.CommandValue,
        DslCompletionRef.Align => CompletionContext.AlignValue,
        _ => CompletionContext.None,
    };

    /// <summary>扫描当前行 token 序列，收集所有已使用的参数名（key= 模式中的 key）。
    /// 仅收集命中 <paramref name="spec"/>.NamedParams 的 key——位置词引用（如 say 的 by）
    /// 不在 NamedParams 中，不会被误收集；未在语法表中的 key（拼写错误）同样不收集，
    /// 保留其补全候选以便用户修正。</summary>
    private static HashSet<string> CollectUsedParamNames(DslToken[] lineTokens, ReadOnlySpan<char> source, DslStmtGrammar spec)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < lineTokens.Length; i++)
        {
            if (lineTokens[i].Kind == DslTokenKind.Symbol && source[lineTokens[i].Offset] == '='
                && lineTokens[i - 1].Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
            {
                var key = lineTokens[i - 1].GetText(source).ToString();
                if (spec.NamedParams.ContainsKey(key))
                    used.Add(key);
            }
        }
        return used;
    }

    /// <summary>光标前是否存在未闭合的 {（表达式插值上下文）。</summary>
    private static bool IsInInterpolation(ReadOnlySpan<char> source, int offset)
    {
        var start = offset;
        while (start > 0 && source[start - 1] != '\n') start--;
        var depth = 0;
        for (var i = start; i < offset; i++)
        {
            var c = source[i];
            if (c == '{') depth++;
            else if (c == '}') depth--;
        }
        return depth > 0;
    }

    private static bool IsArrow(DslToken t, ReadOnlySpan<char> source) =>
        t.Kind == DslTokenKind.Symbol && t.Length == 2 && source[t.Offset] == '-' && source[t.Offset + 1] == '>';

    public SignatureHelpInfo? GetSignatureHelp(string filePath, int offset)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return null;
        var source = doc.Source;
        var line = doc.GetLineIndex(offset);
        var lineStart = doc.GetLineStart(line);
        var lineLen = source.Length - lineStart;
        if (lineLen > 0)
        {
            var nl = source.Slice(lineStart).IndexOf('\n');
            if (nl >= 0) lineLen = nl;
        }
        var lineText = source.Slice(lineStart, lineLen);
        var lineTokens = DslTokenizer.TokenizeLine(lineText, lineStart);

        if (lineTokens.Length == 0) return null;

        // 跳过行首空白，取第一个实质 token 作为关键字
        var kwIdx = 0;
        while (kwIdx < lineTokens.Length && lineTokens[kwIdx].Kind == DslTokenKind.Whitespace)
            kwIdx++;
        if (kwIdx >= lineTokens.Length) return null;

        var kwToken = lineTokens[kwIdx];
        // 光标必须在关键字之后（在关键字本身上不弹签名）
        if (offset <= kwToken.Offset + kwToken.Length) return null;

        var keyword = kwToken.GetText(source).ToString();
        var spec = DslGrammar.TryGet(keyword);
        if (spec == null || spec.NamedParams.Count == 0) return null;

        // 构建参数列表
        var paramInfos = new List<ParameterInfo>(spec.NamedParams.Count);
        var labelParts = new List<string>(spec.NamedParams.Count);
        var paramOrder = new List<string>(spec.NamedParams.Count);
        foreach (var kv in spec.NamedParams)
        {
            paramInfos.Add(new ParameterInfo(kv.Key));
            labelParts.Add($"{kv.Key}=");
            paramOrder.Add(kv.Key);
        }

        var label = $"{keyword}({string.Join(", ", labelParts)})";
        var sig = new SignatureInfo(label, null, paramInfos);

        // 确定 activeParameter：光标前最近的 key= 的参数索引
        int? activeParam = null;
        for (var i = lineTokens.Length - 1; i > kwIdx; i--)
        {
            var t = lineTokens[i];
            if (t.Offset >= offset) continue; // 跳过光标后的 token
            if (t.Kind == DslTokenKind.Symbol && t.Offset < source.Length && source[t.Offset] == '=')
            {
                if (i > 0 && lineTokens[i - 1].Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
                {
                    var paramName = lineTokens[i - 1].GetText(source).ToString();
                    var idx = paramOrder.IndexOf(paramName);
                    if (idx >= 0) activeParam = idx;
                    break;
                }
            }
        }

        return new SignatureHelpInfo([sig], 0, activeParam);
    }

    public HoverInfo? GetHover(string filePath, int offset)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return null;
        var source = doc.Source;

        var occ = _symbolIndex.FindOccurrenceAt(filePath, offset);
        if (occ != null)
        {
            var o = occ.Value;
            if (o.Role == SymbolRole.Definition)
            {
                if (o.IsDeclaration)
                {
                    var refs = _symbolIndex.FindReferencesWithFallback(o.Kind, FallbackKind(o.Kind), o.Name).Count;
                    var scopeTag = o.Kind == SymbolKind.Variable ? VarScopeBadge(o) : null;
                    var detail = o.Kind == SymbolKind.Variable && scopeTag != null
                        ? $"{KindLabel(o.Kind)} 定义（{scopeTag}）\n{refs} 处引用"
                        : $"{KindLabel(o.Kind)} 定义\n{refs} 处引用";
                    return new HoverInfo(o.Name, detail, new Location(o.FilePath, o.Offset, o.Length));
                }

                // set（赋值，非声明式定义）：不标「定义」，而是标「赋值」并指向规范定义（define）。
                var fb = FallbackKind(o.Kind);
                var def = _symbolIndex.Resolve(o.Kind, fb, o.Name, filePath, o.ScopePath);
                var scopeTag2 = o.Kind == SymbolKind.Variable ? VarScopeBadge(o) : null;
                var roleLabel = o.Kind == SymbolKind.Variable
                    ? $"变量 赋值（{scopeTag2}）"
                    : $"{KindLabel(o.Kind)} 赋值";
                if (def != null && (def.Value.FilePath != o.FilePath || def.Value.Offset != o.Offset))
                    roleLabel += $"\n定义于 {def.Value.FilePath}:{LineNumberOf(def.Value.FilePath, def.Value.Offset)}";
                return new HoverInfo(o.Name, roleLabel, new Location(o.FilePath, o.Offset, o.Length));
            }

            var fbRef = FallbackKind(o.Kind);
            var defRef = _symbolIndex.Resolve(o.Kind, fbRef, o.Name, filePath, o.ScopePath);
            if (defRef != null)
            {
                var line = LineNumberOf(defRef.Value.FilePath, defRef.Value.Offset);
                var scopeTag = o.Kind == SymbolKind.Variable ? VarScopeBadge(o) : null;
                var detail = o.Kind == SymbolKind.Variable && scopeTag != null
                    ? $"{KindLabel(o.Kind)} 引用（{scopeTag}）\n定义于 {defRef.Value.FilePath}:{line}"
                    : $"{KindLabel(o.Kind)} 引用\n定义于 {defRef.Value.FilePath}:{line}";
                return new HoverInfo(o.Name, detail, new Location(defRef.Value.FilePath, defRef.Value.Offset, defRef.Value.Length));
            }
            if (o.IsOptional)
                return new HoverInfo(o.Name, $"说话人：{o.Name}",
                    new Location(o.FilePath, o.Offset, o.Length));
            if (DslSymbolIndex.IsInternalVariableName(o.Name))
                return new HoverInfo(o.Name, "变量（内部临时变量）",
                    new Location(o.FilePath, o.Offset, o.Length));
            // C# 命令：展示注册信息（来自 CsSymbolIndex）。
            if (o.Kind == SymbolKind.Command && _projectIndex.GetCommandNames().Contains(o.Name))
                return new HoverInfo(o.Name, $"命令（C# 注册）\n{o.Name}",
                    new Location(o.FilePath, o.Offset, o.Length));
            return new HoverInfo(o.Name, $"未定义的{KindLabel(o.Kind)}「{o.Name}」",
                new Location(o.FilePath, o.Offset, o.Length));
        }

        var token = doc.TokenAt(offset);
        if (token != null)
        {
            if (token.Value.Kind == DslTokenKind.String)
            {
                var res = TryResourceAt(filePath, offset);
                if (res != null)
                {
                    var (value, resolved) = res.Value;
                    var detail = resolved != null
                        ? $"资源文件（{resolved.Kind}）\n{resolved.RelativePath}\n{resolved.FormattedSize}"
                        : $"资源引用（未找到文件）\n{value}";
                    return new HoverInfo(value, detail, new Location(filePath, token.Value.Offset, token.Value.Length));
                }
            }
            var text = token.Value.GetText(source).ToString();
            // 关键字 / 内置函数 / 字面量：优先给出功能说明，而非仅「语义类别」。
            if (token.Value.Kind != DslTokenKind.String && DslKeywordDocs.TryGet(text, out var kd))
            {
                var detail = kd.Usage != null ? $"{kd.Summary}\n\n示例：\n{kd.Usage}" : kd.Summary;
                return new HoverInfo(text, detail, new Location(filePath, token.Value.Offset, token.Value.Length));
            }
            var cat = DslSemanticClassifier.Classify(token.Value, source);
            return new HoverInfo(text, $"语义类别：{SemanticCategoryLabel(cat)}");
        }
        return null;
    }

    public DefinitionResult GoToDefinition(string filePath, int offset)
    {
        var occ = _symbolIndex.FindOccurrenceAt(filePath, offset);
        if (occ == null)
        {
            // 资源路径字符串（image src= / bgm / sprite src= 等）→ 跳转至磁盘文件。
            var res = TryResourceAt(filePath, offset);
            if (res != null && res.Value.Resolved != null)
                return new DefinitionResult(true, new Location(res.Value.Resolved.AbsolutePath, 0, 0), SymbolKind.Scene);
            return new DefinitionResult(false, null, SymbolKind.Scene);
        }
        var o = occ.Value;

        if (o.Role == SymbolRole.Reference)
        {
            var fb = FallbackKind(o.Kind);
            var def = _symbolIndex.Resolve(o.Kind, fb, o.Name, filePath, o.ScopePath);
            if (def != null)
                return new DefinitionResult(true, new Location(def.Value.FilePath, def.Value.Offset, def.Value.Length), def.Value.Kind);
            return new DefinitionResult(false, null, o.Kind);
        }

        // 光标在定义处：set（赋值，非声明式）重定向到规范定义（define）；其余跳到自身。
        if (!o.IsDeclaration)
        {
            var fb = FallbackKind(o.Kind);
            var def = _symbolIndex.Resolve(o.Kind, fb, o.Name, filePath, o.ScopePath);
            if (def != null && (def.Value.FilePath != o.FilePath || def.Value.Offset != o.Offset))
                return new DefinitionResult(true, new Location(def.Value.FilePath, def.Value.Offset, def.Value.Length), def.Value.Kind);
        }
        return new DefinitionResult(true, new Location(o.FilePath, o.Offset, o.Length), o.Kind);
    }

    public ReferenceResult FindReferences(string filePath, int offset)
    {
        var occ = _symbolIndex.FindOccurrenceAt(filePath, offset);
        if (occ == null)
            return new ReferenceResult(System.Array.Empty<Location>(), SymbolKind.Scene, string.Empty);
        var o = occ.Value;
        var fb = FallbackKind(o.Kind);
        // 局部变量(let/local)：引用 / 定义均限定在同文件（不跨文件解析）；其它文件同名局部引用不计入。
        if (o.Kind == SymbolKind.Variable && o.Scope == SymbolScope.Local)
        {
            var def = _symbolIndex.Resolve(o.Kind, fb, o.Name, filePath, o.ScopePath);
            var refs = def != null
                ? new List<Location>(_symbolIndex.FindReferencesWithFallback(o.Kind, fb, o.Name, def.Value.FilePath))
                : new List<Location>();
            if (def != null) refs.Add(new Location(def.Value.FilePath, def.Value.Offset, def.Value.Length));
            return new ReferenceResult(refs, o.Kind, o.Name);
        }
        // 全局符号（define/set/label/scene/func…）：跨文件引用全收集。
        var refsAll = new List<Location>(_symbolIndex.FindReferencesWithFallback(o.Kind, fb, o.Name));
        var gdef = _symbolIndex.Resolve(o.Kind, fb, o.Name);
        if (gdef != null) refsAll.Add(new Location(gdef.Value.FilePath, gdef.Value.Offset, gdef.Value.Length));
        return new ReferenceResult(refsAll, o.Kind, o.Name);
    }

    // ---- LSP 增强特性（rename / documentSymbol / workspaceSymbol / documentHighlight）----

    /// <inheritdoc/>
    public IReadOnlyList<DocumentOutlineSymbol> GetDocumentSymbols(string filePath)
    {
        var defs = _symbolIndex.GetDefinitionsInFile(filePath);
        var nodes = new List<DocumentOutlineSymbol>();
        // scene 名 → 大纲节点，供把 ScopePath="scene/<名>" 的子定义挂为子节点。
        var sceneByName = new Dictionary<string, DocumentOutlineSymbol>(StringComparer.Ordinal);
        foreach (var d in defs)
        {
            var node = new DocumentOutlineSymbol
            {
                Name = d.Name,
                Kind = MapSymbolKindToLsp(d.Kind),
                StartOffset = d.Offset,
                EndOffset = d.Offset + d.Length,
            };
            if (d.ScopePath.StartsWith("scene/", StringComparison.Ordinal))
            {
                // 子定义（如 label）挂到所属 scene；scene 尚未出现（异常顺序）则退化为顶层。
                var sceneName = d.ScopePath.Substring("scene/".Length);
                if (sceneByName.TryGetValue(sceneName, out var parent)) parent.Children.Add(node);
                else nodes.Add(node);
            }
            else
            {
                nodes.Add(node);
                if (d.Kind == SymbolKind.Scene) sceneByName[d.Name] = node;
            }
        }
        // 跨文件场景：VN 引擎的场景本就跨文件分布。把其它文件中声明的场景也并入当前文件大纲，
        // 使大纲成为「项目级场景导航图」。这些节点带自身 FilePath，server 层据此定位到正确文件（详见 CollectOutline）。
        foreach (var d in _symbolIndex.GetAllDefinitions())
        {
            if (d.Kind != SymbolKind.Scene) continue;
            if (string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase)) continue; // 当前文件场景已在上方收入
            nodes.Add(new DocumentOutlineSymbol
            {
                Name = d.Name,
                Kind = MapSymbolKindToLsp(d.Kind),
                StartOffset = d.Offset,
                EndOffset = d.Offset + d.Length,
                FilePath = d.FilePath,
            });
        }
        return nodes;
    }

    /// <inheritdoc/>
    public IReadOnlyList<WorkspaceSymbolInfo> GetWorkspaceSymbols(string query)
    {
        var q = query?.Trim() ?? string.Empty;
        var defs = _symbolIndex.GetAllDefinitions();
        var result = new List<WorkspaceSymbolInfo>();
        foreach (var d in defs)
        {
            if (q.Length > 0 && d.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            result.Add(new WorkspaceSymbolInfo
            {
                Name = d.Name,
                Kind = MapSymbolKindToLsp(d.Kind),
                FilePath = d.FilePath,
                Offset = d.Offset,
                Length = d.Length,
            });
        }
        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<HighlightSpan> GetDocumentHighlights(string filePath, int offset)
    {
        var refs = FindReferences(filePath, offset);
        var list = new List<HighlightSpan>(refs.Locations.Count);
        foreach (var L in refs.Locations)
            list.Add(new HighlightSpan { Offset = L.Offset, Length = L.Length, Kind = 2 });
        return list;
    }

    /// <inheritdoc/>
    public RenameResult? Rename(string filePath, int offset, string newName)
    {
        if (string.IsNullOrEmpty(newName)) return null;
        var refs = FindReferences(filePath, offset);
        if (refs.Locations.Count == 0) return null;
        var changes = new Dictionary<string, List<RenameEdit>>(StringComparer.Ordinal);
        foreach (var L in refs.Locations)
        {
            if (!changes.TryGetValue(L.FilePath, out var list))
            {
                list = new List<RenameEdit>();
                changes[L.FilePath] = list;
            }
            list.Add(new RenameEdit { Offset = L.Offset, Length = L.Length, NewText = newName });
        }
        return new RenameResult { Changes = changes };
    }

    /// <summary>SymbolKind → LSP SymbolKind 数值（仅用于大纲/符号搜索的图标分类）。</summary>
    private static int MapSymbolKindToLsp(SymbolKind kind) => kind switch
    {
        SymbolKind.Scene => 5,      // Class
        SymbolKind.Label => 6,      // Method
        SymbolKind.Func => 12,      // Function
        SymbolKind.Character => 8,  // Field
        SymbolKind.Style => 5,      // Class
        SymbolKind.Command => 12,   // Function
        SymbolKind.Variable => 13,  // Variable
        _ => 13,
    };

    /// <summary>取某文件的规范源码文本（供跳转/悬停等把行/列坐标换算成偏移）。
    /// 优先取 didOpen 内存文本，回退到工作区扫描建立的索引文档——确保「仅被扫描、尚未 didOpen 的文件」也能正确解析坐标。</summary>
    public string? GetSource(string filePath)
        => _documents.TryGetValue(filePath, out var doc) ? doc.Source.ToString() : null;

    /// <inheritdoc/>
    public string? FormatDocument(string filePath, int? tabSize = null, bool insertSpaces = true)
    {
        var source = GetSource(filePath);
        return source is null ? null : DslFormatter.Format(source, tabSize, insertSpaces);
    }

    /// <inheritdoc/>
    public string? FormatRange(string filePath, int startLine, int endLine, int? tabSize = null, bool insertSpaces = true)
    {
        var source = GetSource(filePath);
        return source is null ? null : DslFormatter.FormatRange(source, startLine, endLine, tabSize, insertSpaces);
    }

    public Task<DslAnalysisResult> GetDiagnosticsAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var diags = _symbolIndex.GetDiagnostics(filePath);
        // 资源 / 命令引用诊断 + 重复参数诊断（需文档 token，仅此处可访问）。
        // 仅在文档已加载后执行；未打开的文件只走符号级诊断（未定义引用 + 重复定义）。
        if (_documents.TryGetValue(filePath, out var doc))
        {
            var tokens = doc.GetAllTokens();
            var source = doc.Source;
            var extra = new List<Diagnostic>();
            if (_scanned)
                extra.AddRange(CollectReferenceDiagnostics(filePath, tokens, source));
            extra.AddRange(CollectDuplicateParamDiagnostics(filePath, tokens, source));
            if (extra.Count > 0)
            {
                var merged = new List<Diagnostic>(diags.Count + extra.Count);
                merged.AddRange(diags);
                merged.AddRange(extra);
                diags = merged;
            }
        }
        return Task.FromResult(new DslAnalysisResult(filePath, diags));
    }

    /// <summary>
    /// 资源 / 命令引用诊断：扫描整篇文档的全部字符串字面量与裸标识符值，
    /// 依 <see cref="DslGrammar"/> 判定其语法槽位（资源路径 / 按钮命令），
    /// 再查 <see cref="IProjectIndex"/> 是否真实存在 / 已注册——不存在则给「未找到资源 / 未注册命令」警告。
    /// <para>设计取舍：均给 <see cref="DiagnosticSeverity.Warning"/> 而非 Error——
    /// 因资源相对根可能与引擎解析基不同、命令亦可能由引擎在运行时注册（静态 C# 扫描捕获不到），
    /// 报 Error 会引入误报噪声；如需更严苛可改 Error。插值/表达式值（含 { }）无法静态解析，直接跳过以免误报。</para>
    /// </summary>
    private List<Diagnostic> CollectReferenceDiagnostics(string filePath, DslToken[] tokens, ReadOnlySpan<char> source)
    {
        var result = new List<Diagnostic>();
        if (tokens.Length == 0) return result;

        // 按行分组（token 不跨行），逐行用本行首词解析语法槽位——与 DslSymbolIndex 同法。
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') lineStarts.Add(i + 1);

        var commandSet = new HashSet<string>(_projectIndex.GetCommandNames(), StringComparer.OrdinalIgnoreCase);

        var i2 = 0;
        while (i2 < tokens.Length)
        {
            var lineIdx = LineOf(lineStarts, tokens[i2].Offset);
            var lineEnd = (lineIdx + 1 < lineStarts.Count) ? lineStarts[lineIdx + 1] - 1 : source.Length;
            var start = i2;
            while (i2 < tokens.Length && tokens[i2].Offset < lineEnd) i2++;
            // 处理本行 token [start, i2)
            for (var k = start; k < i2; k++)
            {
                var t = tokens[k];
                if (t.Kind == DslTokenKind.String)
                {
                    var raw = t.GetText(source).ToString();
                    // 仅检查「完整闭合」的引号字面量，避免输入中途（"Audi）被误报。
                    if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"') continue;
                    var value = raw.Substring(1, raw.Length - 2);
                    if (value.Length == 0 || value.IndexOfAny(['{', '}']) >= 0) continue; // 插值/表达式，无法静态解析 → 跳过
                    var refKind = RefForValueToken(tokens, source, k, start);
                    if (refKind == DslCompletionRef.Resource)
                    {
                        if (_projectIndex.FindResource(value) is null)
                            result.Add(new Diagnostic(DiagnosticSeverity.Warning, $"未找到资源：{value}", new Location(filePath, t.Offset, t.Length)));
                    }
                    else if (refKind == DslCompletionRef.Command)
                    {
                        if (!commandSet.Contains(value))
                            result.Add(new Diagnostic(DiagnosticSeverity.Warning, $"未注册命令：{value}", new Location(filePath, t.Offset, t.Length)));
                    }
                }
                else if (t.Kind == DslTokenKind.Identifier)
                {
                    // 裸标识符值：仅语法槽位为 Command 时检查（如 cmd=open_sa）。资源位置（bgm x.mp3）亦同样检查。
                    var refKind = RefForValueToken(tokens, source, k, start);
                    if (refKind == DslCompletionRef.Command || refKind == DslCompletionRef.Resource)
                    {
                        var value = t.GetText(source).ToString();
                        if (value.Length == 0 || value.IndexOfAny(['{', '}']) >= 0) continue;
                        if (refKind == DslCompletionRef.Command)
                        {
                            if (!commandSet.Contains(value))
                                result.Add(new Diagnostic(DiagnosticSeverity.Warning, $"未注册命令：{value}", new Location(filePath, t.Offset, t.Length)));
                        }
                        else if (_projectIndex.FindResource(value) is null)
                        {
                            result.Add(new Diagnostic(DiagnosticSeverity.Warning, $"未找到资源：{value}", new Location(filePath, t.Offset, t.Length)));
                        }
                    }
                }
            }
        }
        return result;
    }

    /// <summary>重复参数诊断：扫描每行 token，同一 key 在同一行内出现 ≥2 次 key= 模式时给 Warning。
    /// 仅针对有 NamedParams 的语句/UI 元素（非声明式语句如 define/set 无 key= 模式，不会误触）。
    /// 设计取舍：给 Warning 而非 Error——引擎运行时静默覆盖后者，不崩溃，只是创作隐患。</summary>
    private static List<Diagnostic> CollectDuplicateParamDiagnostics(string filePath, DslToken[] tokens, ReadOnlySpan<char> source)
    {
        var result = new List<Diagnostic>();
        if (tokens.Length == 0) return result;

        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') lineStarts.Add(i + 1);

        var i2 = 0;
        while (i2 < tokens.Length)
        {
            var lineIdx = LineOf(lineStarts, tokens[i2].Offset);
            var lineEnd = (lineIdx + 1 < lineStarts.Count) ? lineStarts[lineIdx + 1] - 1 : source.Length;
            var start = i2;
            while (i2 < tokens.Length && tokens[i2].Offset < lineEnd) i2++;

            // 收集本行出现的所有 key= 模式中的 key 名及其首次出现偏移
            var seenKeys = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var k = start; k < i2; k++)
            {
                if (tokens[k].Kind == DslTokenKind.Symbol && source[tokens[k].Offset] == '='
                    && k > start
                    && tokens[k - 1].Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
                {
                    var key = tokens[k - 1].GetText(source).ToString();
                    if (seenKeys.TryAdd(key, tokens[k - 1].Offset)) continue;
                    // key 已出现过 → 重复
                    result.Add(new Diagnostic(DiagnosticSeverity.Warning,
                        $"重复参数「{key}」（后者覆盖前者）",
                        new Location(filePath, tokens[k - 1].Offset, tokens[k - 1].Length)));
                }
            }
        }
        return result;
    }

    /// <summary>判定某「值 token」（字符串或裸标识符，位于下标 k）的语法引用种类——与补全 <see cref="ResolveContext"/> 同一套 2a/2b/2c 判定，只是作用于整篇文档的任意 token。</summary>
    private static DslCompletionRef RefForValueToken(DslToken[] tokens, ReadOnlySpan<char> source, int k, int lineStart)
    {
        var spec = DslGrammar.TryGet(tokens[lineStart].GetText(source).ToString());
        // 2a) key= 值：值前最近一个紧邻标识符的 '='
        for (var j = k - 1; j >= lineStart + 1; j--)
        {
            if (tokens[j].Kind == DslTokenKind.Symbol && source[tokens[j].Offset] == '=')
            {
                var prev = tokens[j - 1];
                if (prev.Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
                {
                    var key = prev.GetText(source).ToString();
                    return spec?.NamedParams.TryGetValue(key, out var rv) == true ? rv : DslCompletionRef.None;
                }
            }
        }
        // 2b) 位置词槽：值前紧邻的单词（by / with / scene ...）
        if (k - 1 >= lineStart)
        {
            var prevText = tokens[k - 1].GetText(source).ToString();
            if (spec?.PositionalWordRefs.TryGetValue(prevText, out var rw) == true) return rw;
        }
        // 2c) 首个位置参
        return spec?.PositionalRef ?? DslCompletionRef.None;
    }

    /// <summary>判定某字符串 token 在语法上是否处于「资源路径」槽位（image src= / bgm / sprite src= / live2d_char src= 等）。
    /// 复用 <see cref="RefForValueToken"/> 同一套 2a/2b/2c 判定，作用于该字符串所在整行的 token 序列。</summary>
    private static DslCompletionRef RefOfStringToken(DslDocument doc, DslToken stringToken, ReadOnlySpan<char> source)
    {
        if (stringToken.Kind != DslTokenKind.String) return DslCompletionRef.None;
        var off = stringToken.Offset;
        var lineStart = off;
        while (lineStart > 0 && source[lineStart - 1] != '\n') lineStart--;
        var lineEnd = off;
        while (lineEnd < source.Length && source[lineEnd] != '\n') lineEnd++;
        var lineTokens = DslTokenizer.TokenizeLine(source.Slice(lineStart, lineEnd - lineStart), lineStart);
        var idx = -1;
        for (var i = 0; i < lineTokens.Length; i++)
            if (lineTokens[i].Offset == stringToken.Offset && lineTokens[i].Length == stringToken.Length) { idx = i; break; }
        if (idx < 0) return DslCompletionRef.None;
        return RefForValueToken(lineTokens, source, idx, 0);
    }

    /// <summary>若 offset 落在「资源引用字符串」内（如 image "Images/lingfan.png"），返回其路径与解析到的资源条目（未扫描资源索引则为 null）。</summary>
    private (string Value, ResourceEntry? Resolved)? TryResourceAt(string filePath, int offset)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return null;
        var token = doc.TokenAt(offset);
        if (token == null || token.Value.Kind != DslTokenKind.String) return null;
        var raw = token.Value.GetText(doc.Source).ToString();
        if (raw.Length < 2 || raw[0] != '"' || raw[raw.Length - 1] != '"') return null;
        var value = raw.Substring(1, raw.Length - 2);
        if (value.Length == 0 || value.IndexOfAny(['{', '}']) >= 0) return null; // 插值，无法静态解析
        if (RefOfStringToken(doc, token.Value, doc.Source) != DslCompletionRef.Resource) return null;
        return (value, _scanned ? _projectIndex.FindResource(value) : null);
    }

    /// <summary>在单调递增的 lineStarts 中二分定位 offset 所属行（与 DslSymbolIndex.LineOf 同算法）。</summary>
    private static int LineOf(List<int> lineStarts, int offset)
    {
        var lo = 0;
        var hi = lineStarts.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (lineStarts[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private int LineNumberOf(string filePath, int offset)
    {
        if (_documents.TryGetValue(filePath, out var d)) return d.GetLineIndex(offset) + 1;
        return 1;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetDefinedNames(SymbolKind kind) => _symbolIndex.GetDefinedNames(kind);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetVariablesWithScope() => _symbolIndex.GetVariablesWithScope();

    /// <inheritdoc/>
    public (int Line, int Column) GetLineColumn(string filePath, int offset)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return (1, 1);
        var line = doc.GetLineIndex(offset);
        var col = offset - doc.GetLineStart(line) + 1;
        return (line + 1, col);
    }

    private static string KindLabel(SymbolKind kind) => kind switch
    {
        SymbolKind.Scene => "场景",
        SymbolKind.Label => "标签",
        SymbolKind.Variable => "变量",
        SymbolKind.Character => "角色",
        SymbolKind.Func => "函数",
        SymbolKind.Style => "样式",
        SymbolKind.Command => "命令",
        _ => kind.ToString(),
    };

    /// <summary>语义分类枚举值 → 用户友好的中文标签（hover 显示用）。</summary>
    private static string SemanticCategoryLabel(SemanticCategory cat) => cat switch
    {
        SemanticCategory.ControlFlow => "控制流",
        SemanticCategory.Navigation => "导航",
        SemanticCategory.DataOp => "数据操作",
        SemanticCategory.Media => "媒体",
        SemanticCategory.Display => "显示/动画",
        SemanticCategory.SaveLoad => "存档系统",
        SemanticCategory.Chapter => "章节/成就",
        SemanticCategory.Rollback => "回溯控制",
        SemanticCategory.Playback => "播放控制",
        SemanticCategory.TimeEvent => "时间事件",
        SemanticCategory.Notify => "通知/调试",
        SemanticCategory.UiEnhance => "UI 增强",
        SemanticCategory.UiContainer => "UI 容器",
        SemanticCategory.UiInteractive => "UI 交互",
        SemanticCategory.UiDisplay => "UI 显示",
        SemanticCategory.Parameter => "参数",
        SemanticCategory.ElementAttribute => "元素属性",
        SemanticCategory.Keyword => "关键字",
        SemanticCategory.Function => "内置函数",
        SemanticCategory.Identifier => "标识符",
        SemanticCategory.String => "字符串",
        SemanticCategory.Number => "数字",
        SemanticCategory.Comment => "注释",
        SemanticCategory.Symbol => "符号",
        SemanticCategory.Literal => "字面量",
        SemanticCategory.Resource => "资源路径",
        SemanticCategory.SymbolDefinition => "符号定义",
        SemanticCategory.SymbolReference => "符号引用",
        _ => "未知",
    };

    /// <inheritdoc/>
    public IReadOnlyList<(int Start, int End)> GetFoldingRegions(string filePath)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return System.Array.Empty<(int, int)>();
        // O(1) 命中：同一文档实例 + 内容版本未变（编辑自增 Version → 自动失效），无 O(n) 全文比对。
        if (_foldingCache.TryGetValue(filePath, out var cached) && cached.Doc == doc && cached.Version == doc.Version)
            return cached.Foldings;
        ComputeStructure(doc.Text, out var foldings, out var depths);
        _foldingCache[filePath] = new FoldingCacheEntry(doc, doc.Version, foldings, depths);
        return foldings;
    }

    /// <inheritdoc/>
    public int[] GetLineBlockDepths(string filePath)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return System.Array.Empty<int>();
        if (_foldingCache.TryGetValue(filePath, out var cached) && cached.Doc == doc && cached.Version == doc.Version)
            return cached.Depths;
        ComputeStructure(doc.Text, out var foldings, out var depths);
        _foldingCache[filePath] = new FoldingCacheEntry(doc, doc.Version, foldings, depths);
        return depths;
    }

    /// <summary>
    /// 结构化块分析（单算法产出折叠区段与逐行嵌套深度）——折叠与格式化共用，杜绝漂移。
    /// <para>块头关键字来自 DslCore 单源 <see cref="DslBlockStructure"/>；else/case/default 是对块栈的匹配而非起始块。</para>
    /// </summary>
    private static void ComputeStructure(ReadOnlySpan<char> source, out List<(int Start, int End)> foldings, out int[] depths)
    {
        // 行数（按 '\n' 计），避免 source.Split 给每行 new 字符串的巨量分配——大文件下这是秒级卡顿的主因。
        var lineCount = 1;
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') lineCount++;

        depths = new int[lineCount];
        foldings = new List<(int, int)>(lineCount / 4);
        var lineStarts = new int[lineCount];

        var stack = new Stack<(int Line, int Indent)>();
        var lineStart = 0;
        var li = 0;
        while (li < lineCount)
        {
            lineStarts[li] = lineStart;
            var relNl = source.Slice(lineStart).IndexOf('\n');
            var lineEnd = relNl < 0 ? source.Length : lineStart + relNl;

            // 本行内容（去结尾换行；CRLF 尾部 '\r' 一并剥离），全程 span 不分配
            var span = source.Slice(lineStart, lineEnd - lineStart);
            if (span.Length > 0 && span[span.Length - 1] == '\r') span = span.Slice(0, span.Length - 1);

            var s = 0;
            while (s < span.Length && (span[s] == ' ' || span[s] == '\t')) s++;
            var indent = 0;
            for (var k = 0; k < s; k++) indent += span[k] == '\t' ? 4 : 1;

            var w = s;
            while (w < span.Length && span[w] != ' ' && span[w] != '\t' && span[w] != '#') w++;
            var word = span.Slice(s, w - s);

            if (word.IsEmpty || (span.Length > 0 && span[s] == '#'))
            {
                // 空行或注释行：继承当前块深度，不管理栈
                depths[li] = stack.Count;
            }
            else
            {
                var firstWord = word.ToString(); // 块关键字很短，分配可忽略（与整行 Split/Trim 不可同日而语）
                if (firstWord == "}")
                {
                    // 花括号块关闭：弹栈。
                    if (stack.Count > 0)
                    {
                        var (startLine, _) = stack.Pop();
                        if (li > startLine)
                            foldings.Add((lineStarts[startLine], lineStarts[li]));
                    }
                    // } 后可能跟续行（如 } else {），} 本身深度为弹栈后
                    var afterBrace = span.Slice(w).TrimStart();
                    if (afterBrace.Length > 0)
                    {
                        var spIdx = afterBrace.IndexOf(' ');
                        var afterWord = afterBrace.Slice(0, spIdx >= 0 ? spIdx : afterBrace.Length);
                        var afterStr = afterWord.ToString();
                        if (afterStr is "else" or "case" or "default")
                            depths[li] = stack.Count; // 续行：对齐到父块
                        else if (DslBlockStructure.IsBlockStarter(afterStr))
                        {
                            // } else if { / } else while：弹栈后重新压栈
                            depths[li] = stack.Count;
                            stack.Push((li, indent));
                        }
                        else
                            depths[li] = stack.Count;
                    }
                    else
                        depths[li] = stack.Count;
                }
                else if (DslBlockStructure.IsIndentationBlockEnder(firstWord))
                {
                    // end 关键字：弹栈，对齐到父块
                    if (stack.Count > 0)
                    {
                        var (startLine, _) = stack.Pop();
                        if (li > startLine)
                            foldings.Add((lineStarts[startLine], lineStarts[li]));
                    }
                    depths[li] = stack.Count;
                }
                else if (DslBlockStructure.IsBlockStarter(firstWord))
                {
                    // scene 在已有块内是导航命令（非块起始），不压栈
                    if (firstWord == "scene" && stack.Count > 0)
                    {
                        depths[li] = stack.Count;
                    }
                    else
                    {
                        depths[li] = stack.Count;
                        stack.Push((li, indent));
                    }
                }
                else if (firstWord is "else" or "case" or "default")
                {
                    // 与父块同缩进的续行：不闭合、不加深
                    depths[li] = stack.Count;
                }
                else
                {
                    while (stack.Count > 0 && indent <= stack.Peek().Indent)
                    {
                        var (startLine, _) = stack.Pop();
                        if (li - 1 > startLine)
                            foldings.Add((lineStarts[startLine], lineStarts[li - 1]));
                    }
                    depths[li] = stack.Count;
                }
            }

            lineStart = lineEnd + 1;
            li++;
        }

        while (stack.Count > 0)
        {
            var (startLine, _) = stack.Pop();
            if (lineCount - 1 > startLine)
                foldings.Add((lineStarts[startLine], source.Length));
        }

        foldings.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    /// <summary>计算逐行「包围块」关键字数组（下标 = 行号，值 = 该行之上的栈顶块关键字；无则 null）。
    /// 复用 <see cref="DslBlockStructure.IsBlockStarter"/> 与 <see cref="ComputeStructure"/> 同款缩进栈算法，供补全块级上下文感知。</summary>
    private static string?[] ComputeEnclosingBlocks(string source)
    {
        var lineCount = 1;
        for (var i = 0; i < source.Length; i++) if (source[i] == '\n') lineCount++;
        var result = new string?[lineCount];
        var stack = new Stack<(string Kw, int Indent)>();
        var lineStart = 0;
        for (var li = 0; li < lineCount; li++)
        {
            // 本行的包围块 = 处理本行之前的栈顶
            result[li] = stack.Count > 0 ? stack.Peek().Kw : null;
            var lineEnd = source.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = source.Length;
            var span = source.AsSpan(lineStart, lineEnd - lineStart);
            var s = 0;
            while (s < span.Length && (span[s] == ' ' || span[s] == '\t')) s++;
            var indent = 0;
            for (var k = lineStart; k < lineStart + s; k++) indent += source[k] == '\t' ? 4 : 1;
            var w = s;
            while (w < span.Length && span[w] != ' ' && span[w] != '\t' && span[w] != '#') w++;
            var word = span.Slice(s, w - s);
            if (word.Length > 0)
            {
                var kw = word.ToString();
                if (kw == "}")
                {
                    // 花括号块关闭：弹栈。
                    if (stack.Count > 0) stack.Pop();
                }
                else if (DslBlockStructure.IsIndentationBlockEnder(kw))
                {
                    // end 关键字：弹栈。
                    if (stack.Count > 0) stack.Pop();
                }
                else if (DslBlockStructure.IsBlockStarter(kw))
                {
                    // scene 在已有块内是导航命令（非块起始），不压栈
                    if (kw == "scene" && stack.Count > 0) { /* 导航，不压栈 */ }
                    else stack.Push((kw, indent));
                }
                else if (kw is not ("else" or "case" or "default"))
                {
                    while (stack.Count > 0 && indent <= stack.Peek().Indent) stack.Pop();
                }
            }
            lineStart = lineEnd + 1;
        }
        return result;
    }

    private void RecomputeEnclosing(string filePath, string source) =>
        _enclosingBlocks[filePath] = ComputeEnclosingBlocks(source);
}
