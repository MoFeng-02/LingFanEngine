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
    /// 上下文感知补全——与 SDK 侧 <c>DslCompletionProvider</c> 行为对齐：
    /// 行首→语句关键字+UI 元素；call→函数名；jump/menu→标签名；navigate/scene→场景名；
    /// speaker=/by→角色名；type=/loop=/transition=/easing=/with= 等→枚举/布尔/过渡/缓动；
    /// {→变量名。全部符号候选取自 <c>GetDefinedNames</c>（跨文件索引）。
    /// </summary>
    public IReadOnlyList<CompletionItem> GetCompletion(string filePath, int offset)
    {
        var items = new List<CompletionItem>();
        if (!_documents.TryGetValue(filePath, out var doc)) return items;
        var source = doc.Source;
        var line = doc.GetLineIndex(offset);
        var lineStart = doc.GetLineStart(line);
        var beforeWord = source.Slice(lineStart, offset - lineStart).ToString().TrimEnd();

        switch (GetCompletionContext(beforeWord))
        {
            case CompletionContext.StatementStart:
                AddKeywords(items, DslKeywords.Statements, "statement");
                AddKeywords(items, DslKeywords.UiElementTypes, "statement");
                break;

            case CompletionContext.ParameterName:
                AddKeywords(items, DslKeywords.Parameters, "parameter");
                AddKeywords(items, DslKeywords.ElementAttributes, "parameter");
                break;

            case CompletionContext.VariableReference:
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Variable))
                    items.Add(new CompletionItem(n, n, "variable"));
                break;

            case CompletionContext.SceneName:
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Scene))
                    items.Add(new CompletionItem($"\"{n}\"", n, "scene"));
                break;

            case CompletionContext.LabelName:
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Label))
                    items.Add(new CompletionItem(n, n, "label"));
                break;

            case CompletionContext.FuncName:
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Func))
                    items.Add(new CompletionItem(n, n, "func"));
                break;

            case CompletionContext.SpeakerName:
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Character))
                    items.Add(new CompletionItem($"\"{n}\"", n, "character"));
                break;

            case CompletionContext.EnumValue:
                foreach (var v in s_sceneTypes) items.Add(new CompletionItem(v, v, "variable"));
                break;

            case CompletionContext.BooleanValue:
                items.Add(new CompletionItem("true", "true", "variable"));
                items.Add(new CompletionItem("false", "false", "variable"));
                break;

            case CompletionContext.TrueOnlyValue:
                items.Add(new CompletionItem("true", "true", "variable"));
                break;

            case CompletionContext.TransitionValue:
                foreach (var v in DslTransitionNames.All) items.Add(new CompletionItem(v, v, "variable"));
                break;

            case CompletionContext.EasingValue:
                foreach (var v in DslEasingNames.All) items.Add(new CompletionItem(v, v, "variable"));
                break;

            case CompletionContext.General:
            default:
                AddKeywords(items, DslKeywords.Statements, "statement");
                AddKeywords(items, DslKeywords.UiElementTypes, "statement");
                foreach (var n in _symbolIndex.GetDefinedNames(SymbolKind.Variable))
                    items.Add(new CompletionItem(n, n, "variable"));
                break;
        }

        return items;
    }

    private static readonly string[] s_sceneTypes = { "game", "menu", "ui" };
    private static readonly HashSet<string> s_booleanParams = new() { "loop", "autoplay", "skipable", "screenshot", "mask", "unlock" };
    private static readonly HashSet<string> s_trueOnlyParams = new() { "clickable", "noskip", "instant", "typewriter" };

    /// <summary>补全上下文（对齐 SDK DslCompletionProvider.GetCompletionContext）。</summary>
    private enum CompletionContext
    {
        StatementStart, ParameterName, VariableReference, SceneName, LabelName,
        FuncName, SpeakerName, EnumValue, BooleanValue, TrueOnlyValue,
        TransitionValue, EasingValue, General,
    }

    private static void AddKeywords(List<CompletionItem> items, IReadOnlySet<string> keywords, string kind)
    {
        foreach (var kw in keywords) items.Add(new CompletionItem(kw, kw, kind));
    }

    private static CompletionContext GetCompletionContext(string beforeWord)
    {
        var lower = beforeWord.ToLowerInvariant();

        // 若光标处于未闭合字符串内：看引号前的语句关键字 / 参数名
        var q = lower.LastIndexOf('"');
        if (q >= 0 && CountQuotes(lower) % 2 == 1)
        {
            // 字符串内 {var} 插值 → 变量名补全（保留既有行为，SDK 未覆盖）
            if (lower.IndexOf('{') > q) return CompletionContext.VariableReference;
            var beforeString = lower.Substring(0, q).TrimEnd();
            if (IsLastWord(beforeString, "navigate") || IsLastWord(beforeString, "scene")) return CompletionContext.SceneName;
            if (IsLastWord(beforeString, "jump")) return CompletionContext.LabelName;
            if (IsLastWord(beforeString, "call")) return CompletionContext.FuncName;
            if (IsLastWord(beforeString, "by")) return CompletionContext.SpeakerName;
            if (IsLastWord(beforeString, "with")) return CompletionContext.TransitionValue;
            if (beforeString.EndsWith("speaker=")) return CompletionContext.SpeakerName;
            if (beforeString.EndsWith("="))
            {
                var vt = ValueContextFor(ExtractParam(beforeString.Substring(0, beforeString.Length - 1)));
                if (vt != CompletionContext.General) return vt;
            }
            return CompletionContext.General; // 对话文本等字符串内 → 无补全
        }

        // key= 之后 → 值枚举 / 布尔
        if (lower.EndsWith("="))
        {
            var vt = ValueContextFor(ExtractParam(lower.Substring(0, lower.Length - 1)));
            if (vt != CompletionContext.General) return vt;
        }

        if (lower.Length == 0) return CompletionContext.StatementStart;
        if (IsLastWord(lower, "call")) return CompletionContext.FuncName;
        if (IsLastWord(lower, "jump")) return CompletionContext.LabelName;
        if (IsLastWord(lower, "menu")) return CompletionContext.LabelName;
        if (IsLastWord(lower, "navigate") || IsLastWord(lower, "scene")) return CompletionContext.SceneName;
        if (IsLastWord(lower, "by") || lower.EndsWith("speaker=")) return CompletionContext.SpeakerName;
        if (IsLastWord(lower, "with")) return CompletionContext.TransitionValue;
        if (lower.EndsWith("{")) return CompletionContext.VariableReference;
        if (HasParameterContext(lower)) return CompletionContext.ParameterName;
        return CompletionContext.General;
    }

    private static CompletionContext ValueContextFor(string param)
    {
        switch (param)
        {
            case "type": return CompletionContext.EnumValue;
            case "transition": return CompletionContext.TransitionValue;
            case "easing": return CompletionContext.EasingValue;
            default:
                if (s_trueOnlyParams.Contains(param)) return CompletionContext.TrueOnlyValue;
                if (s_booleanParams.Contains(param)) return CompletionContext.BooleanValue;
                return CompletionContext.General;
        }
    }

    private static int CountQuotes(string s)
    {
        var c = 0;
        for (var i = 0; i < s.Length; i++) if (s[i] == '"') c++;
        return c;
    }

    private static string ExtractParam(string text)
    {
        text = text.TrimEnd();
        var sp = text.LastIndexOf(' ');
        return sp >= 0 ? text.Substring(sp + 1) : text;
    }

    private static bool IsLastWord(string text, string word)
    {
        if (text.Length < word.Length) return false;
        if (text.Length == word.Length) return text == word;
        return text.EndsWith(word) && text[text.Length - word.Length - 1] == ' ';
    }

    private static bool HasParameterContext(string beforeWord)
    {
        var parts = beforeWord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        return DslKeywords.Statements.Contains(parts[0]);
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
                var refs = _symbolIndex.FindReferences(o.Kind, o.Name).Count;
                return new HoverInfo(o.Name, $"{KindLabel(o.Kind)} 定义\n{refs} 处引用",
                    new Location(o.FilePath, o.Offset, o.Length));
            }

            var fb = o.Kind == SymbolKind.Label ? SymbolKind.Scene : (SymbolKind?)null;
            var def = _symbolIndex.Resolve(o.Kind, fb, o.Name);
            if (def != null)
            {
                var line = LineNumberOf(def.Value.FilePath, def.Value.Offset);
                return new HoverInfo(o.Name, $"{KindLabel(o.Kind)} 引用\n定义于 {def.Value.FilePath}:{line}",
                    new Location(def.Value.FilePath, def.Value.Offset, def.Value.Length));
            }
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
            var fb = o.Kind == SymbolKind.Label ? SymbolKind.Scene : (SymbolKind?)null;
            var def = _symbolIndex.Resolve(o.Kind, fb, o.Name);
            if (def != null)
                return new DefinitionResult(true, new Location(def.Value.FilePath, def.Value.Offset, def.Value.Length), def.Value.Kind);
            return new DefinitionResult(false, null, o.Kind);
        }

        // 光标在定义处：跳到自身定义位置
        return new DefinitionResult(true, new Location(o.FilePath, o.Offset, o.Length), o.Kind);
    }

    public ReferenceResult FindReferences(string filePath, int offset)
    {
        var occ = _symbolIndex.FindOccurrenceAt(filePath, offset);
        if (occ == null)
            return new ReferenceResult(System.Array.Empty<Location>(), SymbolKind.Scene, string.Empty);
        var o = occ.Value;
        var refs = new List<Location>(_symbolIndex.FindReferences(o.Kind, o.Name));
        var fb = o.Kind == SymbolKind.Label ? SymbolKind.Scene : (SymbolKind?)null;
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
