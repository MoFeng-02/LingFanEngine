using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.DslCore;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// <see cref="IDslLanguageService"/> 的引擎侧实现——只依赖 DslCore，不引用任何 UI 框架。
/// <para>规划 §2.1/§3：承载 tokenizer / 语义高亮 / 补全 / 悬浮 / 跳转 / 查找引用 / 诊断，
/// 并维护跨文件 <see cref="DslSymbolIndex"/>；EngineCore 不引用本工程，依赖单向 DslCore ← LanguageService ← SDK。</para>
/// </summary>
public sealed class DslLanguageService : IDslLanguageService
{
    private readonly Dictionary<string, DslDocument> _documents = new();
    private readonly DslSymbolIndex _symbolIndex = new();

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
    }

    public void RemoveDocument(string filePath)
    {
        _documents.Remove(filePath);
        _symbolIndex.RemoveFile(filePath);
    }

    public void IndexProject(IReadOnlyList<(string Path, string Text)> files)
    {
        foreach (var (path, text) in files)
        {
            var doc = new DslDocument(path, text);
            _documents[path] = doc;
            _symbolIndex.IndexFile(path, doc.GetAllTokens(), doc.Source);
        }
    }

    public IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return System.Array.Empty<SemanticToken>();
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
            result[i] = new SemanticToken(tokens[i].Offset, tokens[i].Length, cat);
        }
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

        // 当前行文本（含换行前）
        var lineLen = source.Length - lineStart;
        if (lineLen > 0)
        {
            var nl = source.Slice(lineStart).IndexOf('\n');
            if (nl >= 0) lineLen = nl;
        }
        var lineText = source.Slice(lineStart, lineLen);
        var lineTokens = DslTokenizer.TokenizeLine(lineText, lineStart);

        var (ctx, spec) = ResolveContext(lineTokens, source, offset);
        switch (ctx)
        {
            case CompletionContext.StatementStart:
                AddKeywords(items, DslKeywords.Statements, "statement");
                AddKeywords(items, DslKeywords.UiElementTypes, "statement");
                break;

            case CompletionContext.ParameterName:
                if (spec != null)
                    foreach (var kv in spec.NamedParams)
                        items.Add(new CompletionItem(kv.Key, kv.Key, "parameter"));
                break;

            case CompletionContext.VariableReference:
                foreach (var (n, sc) in _symbolIndex.GetVariablesWithScope())
                    items.Add(new CompletionItem(n, n, "variable", ScopeBadge(sc)));
                break;

            case CompletionContext.SceneName:
                // navigate 目标可以是 scene 或 label；scene 定义
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Scene))
                    items.Add(new CompletionItem($"\"{n}\"", n, "scene"));
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Label))
                    items.Add(new CompletionItem(n, n, "label"));
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

            case CompletionContext.None:
            default:
                // 自由文本 / 数字 / 对话正文等无补全上下文：不弹，避免干扰输入。
                break;
        }

        return items;
    }

    private static readonly string[] s_sceneTypes = { "game", "menu", "ui" };

    /// <summary>补全上下文（语法驱动，取代旧字符串切片启发式）。</summary>
    private enum CompletionContext
    {
        StatementStart, ParameterName, VariableReference, SceneName, LabelName,
        FuncName, SpeakerName, CharacterName, StyleName, EnumValue, BooleanValue,
        TrueOnlyValue, TransitionValue, EasingValue, None,
    }

    private static void AddKeywords(List<CompletionItem> items, IReadOnlySet<string> keywords, string kind)
    {
        foreach (var kw in keywords) items.Add(new CompletionItem(kw, kw, kind));
    }

    /// <summary>变量作用域徽标（B32）：局部变量标「局部」，其余（define / 仅 set）标「全局」。</summary>
    private static string ScopeBadge(SymbolScope scope) => scope == SymbolScope.Local ? "局部" : "全局";

    /// <summary>定义引用的回退种类：navigate 目标可以是 scene 或 label；jump/menu 目标可以是 label 或 scene；call 目标可以是 func 或 label。</summary>
    private static SymbolKind? FallbackKind(SymbolKind kind) => kind switch
    {
        SymbolKind.Label => SymbolKind.Scene,
        SymbolKind.Scene => SymbolKind.Label,
        SymbolKind.Func => SymbolKind.Label,
        _ => null,
    };

    /// <summary>查询某变量名的作用域（用于悬浮信息标注），查不到则按全局处理。</summary>
    private SymbolScope GetVarScope(string name)
    {
        var scopes = _symbolIndex.GetVariablesWithScope();
        return scopes.TryGetValue(name, out var s) ? s : SymbolScope.Global;
    }

    // ===== 语法驱动的补全上下文判定（取代 GetCompletionContext 字符串切片）=====

    /// <summary>
    /// 解析光标所在行的补全上下文。先判表达式插值（{ 未闭合）→ 变量；
    /// 再判光标是否在引号字符串内→按语法槽位取引用；否则按 token 序列判定行首/key=值/位置词/参数名/裸标识符目标。
    /// </summary>
    private static (CompletionContext Ctx, DslStmtGrammar? Spec) ResolveContext(DslToken[] tokens, ReadOnlySpan<char> source, int offset)
    {
        if (tokens.Length == 0) return (CompletionContext.StatementStart, null);

        // 1) 表达式插值上下文（{ ... 未闭合）→ 变量名。优先级最高（say "{x}" 既在字符串内又在插值内）。
        if (IsInInterpolation(source, offset))
            return (CompletionContext.VariableReference, null);

        // 2) 光标位于引号字符串内部（open quote 之后）→ 按该字符串的语法槽位取引用
        for (var si = 0; si < tokens.Length; si++)
        {
            var t = tokens[si];
            if (t.Kind == DslTokenKind.String && offset > t.Offset && offset <= t.Offset + t.Length)
            {
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
                            return (RefToContext(r), spec);
                        }
                    }
                }
                // 2b) 位置词槽：字符串前紧邻的单词（by / with / scene ...）
                if (si - 1 >= 0)
                {
                    var prevText = tokens[si - 1].GetText(source).ToString();
                    if (spec?.PositionalWordRefs.TryGetValue(prevText, out var rw) == true)
                        return (RefToContext(rw), spec);
                }
                // 2c) 首个位置参
                return (RefToContext(spec?.PositionalRef ?? DslCompletionRef.None), spec);
            }
        }

        // 3) 非字符串、非插值：按 token 序列判定
        var first = tokens[0];
        var firstName = first.GetText(source).ToString();
        var spec2 = DslGrammar.TryGet(firstName);

        // 正在输入行首第一个词（光标仍在该词内，未带尾随空格）→ 语句/元素候选
        if (offset <= first.Offset + first.Length)
            return (CompletionContext.StatementStart, null);

        // key= 值补全：光标前最近 '=' 紧邻一个标识符（参数名）
        for (var i = tokens.Length - 1; i >= 1; i--)
        {
            if (tokens[i].Kind == DslTokenKind.Symbol && source[tokens[i].Offset] == '=')
            {
                var prev = tokens[i - 1];
                if (prev.Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
                {
                    var key = prev.GetText(source).ToString();
                    var r = spec2?.NamedParams.TryGetValue(key, out var rv) == true ? rv : DslCompletionRef.None;
                    return (RefToContext(r), spec2);
                }
            }
        }

        // menu 选项目标："text" -> label
        for (var i = tokens.Length - 1; i >= 0; i--)
        {
            if (tokens[i].Kind == DslTokenKind.Symbol && IsArrow(tokens[i], source))
                return (CompletionContext.LabelName, spec2);
        }

        // 正在输入参数名（key，未到 =）或该语句有命名参数 → 给参数名候选
        if (spec2 != null && spec2.NamedParams.Count > 0)
            return (CompletionContext.ParameterName, spec2);

        // 第一个位置参为裸标识符目标（jump/call/label/func）→ 标签/函数名
        if (spec2 != null && spec2.PositionalRef is DslCompletionRef.Label or DslCompletionRef.Func or DslCompletionRef.LabelOrFunc)
            return (RefToContext(spec2.PositionalRef), spec2);

        // UI 元素属性键
        if (spec2 is { IsUiElement: true })
            return (CompletionContext.ParameterName, spec2);

        return (CompletionContext.None, spec2);
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
                    var scopeTag = o.Kind == SymbolKind.Variable ? ScopeBadge(GetVarScope(o.Name)) : null;
                    var detail = o.Kind == SymbolKind.Variable && scopeTag != null
                        ? $"{KindLabel(o.Kind)} 定义（{scopeTag}）\n{refs} 处引用"
                        : $"{KindLabel(o.Kind)} 定义\n{refs} 处引用";
                    return new HoverInfo(o.Name, detail, new Location(o.FilePath, o.Offset, o.Length));
                }

                // set（赋值，非声明式定义）：不标「定义」，而是标「赋值」并指向规范定义（define）。
                var fb = FallbackKind(o.Kind);
                var def = _symbolIndex.Resolve(o.Kind, fb, o.Name);
                var scopeTag2 = o.Kind == SymbolKind.Variable ? ScopeBadge(GetVarScope(o.Name)) : null;
                var roleLabel = o.Kind == SymbolKind.Variable
                    ? $"变量 赋值（{scopeTag2}）"
                    : $"{KindLabel(o.Kind)} 赋值";
                if (def != null && (def.Value.FilePath != o.FilePath || def.Value.Offset != o.Offset))
                    roleLabel += $"\n定义于 {def.Value.FilePath}:{LineNumberOf(def.Value.FilePath, def.Value.Offset)}";
                return new HoverInfo(o.Name, roleLabel, new Location(o.FilePath, o.Offset, o.Length));
            }

            var fbRef = FallbackKind(o.Kind);
            var defRef = _symbolIndex.Resolve(o.Kind, fbRef, o.Name);
            if (defRef != null)
            {
                var line = LineNumberOf(defRef.Value.FilePath, defRef.Value.Offset);
                var scopeTag = o.Kind == SymbolKind.Variable ? ScopeBadge(GetVarScope(o.Name)) : null;
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
            var cat = DslSemanticClassifier.Classify(token.Value, source);
            var text = token.Value.GetText(source).ToString();
            return new HoverInfo(text, $"语义类别：{cat}");
        }
        return null;
    }

    public DefinitionResult GoToDefinition(string filePath, int offset)
    {
        var occ = _symbolIndex.FindOccurrenceAt(filePath, offset);
        if (occ == null) return new DefinitionResult(false, null, SymbolKind.Scene);
        var o = occ.Value;

        if (o.Role == SymbolRole.Reference)
        {
            var fb = FallbackKind(o.Kind);
            var def = _symbolIndex.Resolve(o.Kind, fb, o.Name);
            if (def != null)
                return new DefinitionResult(true, new Location(def.Value.FilePath, def.Value.Offset, def.Value.Length), def.Value.Kind);
            return new DefinitionResult(false, null, o.Kind);
        }

        // 光标在定义处：set（赋值，非声明式）重定向到规范定义（define）；其余跳到自身。
        if (!o.IsDeclaration)
        {
            var fb = FallbackKind(o.Kind);
            var def = _symbolIndex.Resolve(o.Kind, fb, o.Name);
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
        var refs = new List<Location>(_symbolIndex.FindReferences(o.Kind, o.Name));
        var fb = FallbackKind(o.Kind);
        var def = _symbolIndex.Resolve(o.Kind, fb, o.Name);
        if (def != null) refs.Add(new Location(def.Value.FilePath, def.Value.Offset, def.Value.Length));
        return new ReferenceResult(refs, o.Kind, o.Name);
    }

    public Task<DslAnalysisResult> GetDiagnosticsAsync(string filePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var diags = _symbolIndex.GetDiagnostics(filePath);
        return Task.FromResult(new DslAnalysisResult(filePath, diags));
    }

    private int LineNumberOf(string filePath, int offset)
    {
        if (_documents.TryGetValue(filePath, out var d)) return d.GetLineIndex(offset) + 1;
        return 1;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetDefinedNames(SymbolKind kind) => _symbolIndex.GetDefinedNames(kind);

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, SymbolScope> GetVariablesWithScope() => _symbolIndex.GetVariablesWithScope();

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
        ComputeStructure(doc.Source.ToString(), out var foldings, out _);
        return foldings;
    }

    /// <inheritdoc/>
    public int[] GetLineBlockDepths(string filePath)
    {
        if (!_documents.TryGetValue(filePath, out var doc)) return System.Array.Empty<int>();
        ComputeStructure(doc.Source.ToString(), out _, out var depths);
        return depths;
    }

    /// <summary>
    /// 结构化块分析（单算法产出折叠区段与逐行嵌套深度）——折叠与格式化共用，杜绝漂移。
    /// <para>块头关键字来自 DslCore 单源 <see cref="DslBlockStructure"/>；else/case/default 是对块栈的匹配而非起始块。</para>
    /// </summary>
    private static void ComputeStructure(string source, out List<(int Start, int End)> foldings, out int[] depths)
    {
        var lines = source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        depths = new int[lines.Length];
        foldings = new List<(int, int)>(lines.Length / 4);

        // 绝对行首偏移（含换行长度）
        var lineStarts = new int[lines.Length];
        var off = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            lineStarts[i] = off;
            off += lines[i].Length;
            if (i < lines.Length - 1) off++; // 一个换行符
        }

        var stack = new Stack<(int Line, int Indent)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                depths[i] = stack.Count;
                continue;
            }

            var indent = CountIndent(raw);
            var firstWord = GetFirstWord(trimmed);

            if (DslBlockStructure.IsBlockStarter(firstWord))
            {
                depths[i] = stack.Count;
                stack.Push((i, indent));
            }
            else if (firstWord is "else" or "case" or "default")
            {
                // 与父块同缩进的续行：不闭合、不加深
                depths[i] = stack.Count;
            }
            else
            {
                while (stack.Count > 0 && indent <= stack.Peek().Indent)
                {
                    var (startLine, _) = stack.Pop();
                    if (i - 1 > startLine)
                        foldings.Add((lineStarts[startLine], lineStarts[i - 1]));
                }
                depths[i] = stack.Count;
            }
        }

        while (stack.Count > 0)
        {
            var (startLine, _) = stack.Pop();
            if (lines.Length - 1 > startLine)
                foldings.Add((lineStarts[startLine], source.Length));
        }

        foldings.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    private static int CountIndent(string line)
    {
        var count = 0;
        foreach (var c in line)
        {
            if (c == ' ') count++;
            else if (c == '\t') count += 4;
            else break;
        }
        return count;
    }

    private static string GetFirstWord(string trimmedLine)
    {
        var spaceIdx = trimmedLine.IndexOf(' ');
        return spaceIdx < 0 ? trimmedLine : trimmedLine[..spaceIdx];
    }
}
