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
    private readonly ConcurrentDictionary<string, DslDocument> _documents = new();
    private DslSymbolIndex _symbolIndex = new();
    private readonly IProjectIndex _projectIndex;
    /// <summary>每文件「逐行包围块关键字」缓存——供补全的块级上下文感知（scene 块内首词优先 UI 元素）。
    /// 在 UpdateDocument/IndexProject 重索引后同步重算；典型 .story 文件很小，开销可忽略。</summary>
    private readonly ConcurrentDictionary<string, string?[]> _enclosingBlocks = new();
    /// <summary>折叠结构缓存（区段 + 逐行深度），按「文档实例 + 内容版本号」O(1) 判失效（编辑自增 <see cref="DslDocument.Version"/> → 自动失效）。
    /// 后台 <see cref="IndexProject"/> 建索引后预热，使首次 <c>foldingRange</c> 请求直接命中 → 瞬时（<see cref="ComputeStructure"/> 改 span 零分配 + 缓存命中，大文件不再每次重算 + Split 逐行分配）。</summary>
    private readonly ConcurrentDictionary<string, FoldingCacheEntry> _foldingCache = new();
    /// <summary>语义 token 缓存，按「文档实例 + 内容版本号 + 当前符号索引实例」O(1) 判失效（符号索引交换后旧条目自动失效）。
    /// 同样在 <see cref="IndexProject"/> 预热，使首次 <c>semanticTokens/full</c> 直接命中。</summary>
    private readonly ConcurrentDictionary<string, SemanticCacheEntry> _semanticCache = new();

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
                    // scene 块内：首词大概率是 UI 元素类型（text/button/image/...）——优先列出，再补场景块内常见语句。
                    AddKeywords(items, DslKeywords.UiElementTypes, "element");
                    foreach (var kw in s_sceneBlockStatements) items.Add(Kw(kw, "statement"));
                }
                else
                {
                    AddKeywords(items, DslKeywords.Statements, "statement");
                    AddKeywords(items, DslKeywords.UiElementTypes, "statement");
                }
                break;

            case CompletionContext.ParameterName:
                if (spec != null)
                    foreach (var kv in spec.NamedParams)
                        items.Add(Kw(kv.Key, "parameter"));
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

        return items;
    }

    private static readonly string[] s_sceneTypes = { "game", "menu", "ui" };

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

    private static void AddKeywords(List<CompletionItem> items, IReadOnlySet<string> keywords, string kind)
    {
        foreach (var kw in keywords) items.Add(Kw(kw, kind));
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
            if (t.Kind == DslTokenKind.String && offset > t.Offset && offset <= t.Offset + t.Length)
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

        // key= 值补全：光标前最近 '=' 紧邻一个标识符（参数名）
        for (var i = tokens.Length - 1; i >= 1; i--)
        {
            if (tokens[i].Kind == DslTokenKind.Symbol && source[tokens[i].Offset] == '=')
            {
                var prev = tokens[i - 1];
                if (prev.Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
                {
                    // 值起点 = '=' 之后（如 cmd=open_sa → 替换 "open_sa"）
                    var valueStart = tokens[i].Offset + 1;
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
                    var refs = _symbolIndex.FindReferences(o.Kind, o.Name).Count;
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
            if (DslSymbolIndex.IsInternalVariableName(o.Name))
                return new HoverInfo(o.Name, "变量（内部临时变量）",
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
            return new HoverInfo(text, $"语义类别：{cat}");
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
                ? new List<Location>(_symbolIndex.FindReferences(o.Kind, o.Name, def.Value.FilePath))
                : new List<Location>();
            if (def != null) refs.Add(new Location(def.Value.FilePath, def.Value.Offset, def.Value.Length));
            return new ReferenceResult(refs, o.Kind, o.Name);
        }
        // 全局符号（define/set/label/scene/func…）：跨文件引用全收集。
        var refsAll = new List<Location>(_symbolIndex.FindReferences(o.Kind, o.Name));
        var gdef = _symbolIndex.Resolve(o.Kind, fb, o.Name);
        if (gdef != null) refsAll.Add(new Location(gdef.Value.FilePath, gdef.Value.Offset, gdef.Value.Length));
        return new ReferenceResult(refsAll, o.Kind, o.Name);
    }

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
        // 资源 / 命令引用诊断（需 IProjectIndex，仅本处可访问；_symbolIndex 不引用 ProjectIndex）。
        // 仅在项目资源索引已建立（ScanProject 成功）后判定，避免「未扫描 → 全盘误报未找到资源」的风暴。
        if (_scanned && _documents.TryGetValue(filePath, out var doc))
        {
            var extra = CollectReferenceDiagnostics(filePath, doc.GetAllTokens(), doc.Source);
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
        _ => kind.ToString(),
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
                if (DslBlockStructure.IsBlockStarter(firstWord))
                {
                    depths[li] = stack.Count;
                    stack.Push((li, indent));
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
                if (DslBlockStructure.IsBlockStarter(kw))
                    stack.Push((kw, indent));
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
