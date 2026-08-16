using System.Collections.Generic;
using LingFanEngine.DslCore;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// 跨文件符号索引——构建「定义站点 + 引用站点」映射，是跳转定义 / 查找引用 / 诊断的事实基础。
/// <para>规划 §3.4：token 层只做「这是什么」的分类，符号层做「它指向谁」的语义解析；两者都只依赖 DslCore 词汇，不依赖 UI。</para>
/// <para>NativeAOT 友好：纯字典 / 列表，无反射。</para>
/// </summary>
public sealed class DslSymbolIndex
{
    // (种类, 名称) -> 定义站点（同名取首个定义）
    private readonly Dictionary<SymbolKey, SymbolOccurrence> _definitions = new();
    // (种类, 名称) -> 所有引用站点
    private readonly Dictionary<SymbolKey, List<SymbolOccurrence>> _references = new();
    // 文件 -> 该文件内全部出现（用于重索引时清理旧条目）
    private readonly Dictionary<string, List<SymbolOccurrence>> _byFile = new();

    /// <summary>索引（或重索引）单个文件：先清理旧条目，再解析新条目并入全局表。</summary>
    public void IndexFile(string filePath, DslToken[] tokens, ReadOnlySpan<char> source)
    {
        if (_byFile.TryGetValue(filePath, out var old))
        {
            foreach (var o in old) RemoveOccurrence(o);
            _byFile.Remove(filePath);
        }

        var occurrences = CollectOccurrences(filePath, tokens, source);
        _byFile[filePath] = occurrences;
        foreach (var o in occurrences) AddOccurrence(o);
    }

    /// <summary>行级增量重索引（B23 治根）：仅清理旧受影响片段、重索引受影响行、尾部偏移平移 +delta。
    /// 不再全量 GetAllTokens 还原，也不再全文重扫 lineStarts——整体复杂度从 O(全文) 降到 O(变更)。</summary>
    /// <param name="affectedLines">受影响行的绝对偏移 token（由 DslDocument 增量产出，仅这几行）。</param>
    /// <param name="affectedStartOld">受影响首行在旧文本中的绝对起始偏移。</param>
    /// <param name="oldAffectedEnd">旧受影响区域末尾（不含）绝对偏移。</param>
    /// <param name="delta">文本长度变化（new - old），尾部出现站点据此平移。</param>
    public void IndexFileIncremental(string filePath, DslToken[][] affectedLines, ReadOnlySpan<char> source, int affectedStartOld, int oldAffectedEnd, int delta)
    {
        if (!_byFile.TryGetValue(filePath, out var list)) return;   // 理论上 doc 已存在，不会走到

        // 1) 清理旧受影响片段 [affectedStartOld, oldAffectedEnd)——_byFile 按 Offset 升序，二叉定位连续区间
        var lo = LowerBound(list, affectedStartOld);
        var hi = LowerBound(list, oldAffectedEnd);
        for (var i = lo; i < hi; i++) RemoveOccurrence(list[i]);
        if (hi > lo) list.RemoveRange(lo, hi - lo);

        // 3) 尾部行平移（旧绝对偏移整体 +delta；内容未变，仅位置后移）——先于插入新片段，避免把新片段误当尾部
        var tailLo = LowerBound(list, oldAffectedEnd);
        for (var i = tailLo; i < list.Count; i++)
        {
            var o = list[i];
            RemoveOccurrence(o);
            list[i] = new SymbolOccurrence(o.Kind, o.Role, o.Name, o.FilePath, o.Offset + delta, o.Length, o.Scope);
            AddOccurrence(list[i]);
        }

        // 2) 受影响行增量重索引（仅这几行，无全文 lineStarts 重扫）
        var newOcc = CollectOccurrencesForLines(filePath, affectedLines, source);
        foreach (var o in newOcc) AddOccurrence(o);
        if (newOcc.Count > 0) list.InsertRange(lo, newOcc);
    }

    /// <summary>移除某文件的全部索引条目（文件关闭 / 删除时调用）。</summary>
    public void RemoveFile(string filePath)
    {
        if (_byFile.TryGetValue(filePath, out var old))
        {
            foreach (var o in old) RemoveOccurrence(o);
            _byFile.Remove(filePath);
        }
    }

    /// <summary>解析定义位置（用于跳转定义）。支持 fallback 种类（如 jump 可指向 label 也可指向 scene）。</summary>
    public SymbolOccurrence? Resolve(SymbolKind primary, SymbolKind? fallback, string name)
    {
        if (_definitions.TryGetValue(new SymbolKey(primary, name), out var def)) return def;
        if (fallback is { } fb && _definitions.TryGetValue(new SymbolKey(fb, name), out var fbDef)) return fbDef;
        return null;
    }

    /// <summary>收集某符号的所有引用位置（用于查找所有引用）。</summary>
    public IReadOnlyList<Location> FindReferences(SymbolKind kind, string name)
    {
        if (!_references.TryGetValue(new SymbolKey(kind, name), out var list))
            return System.Array.Empty<Location>();
        var locations = new Location[list.Count];
        for (var i = 0; i < list.Count; i++)
            locations[i] = new Location(list[i].FilePath, list[i].Offset, list[i].Length);
        return locations;
    }

    /// <summary>查找光标位置命中的符号出现（定义或引用），用于悬浮 / 跳转 / 查找引用入口。</summary>
    public SymbolOccurrence? FindOccurrenceAt(string filePath, int offset)
    {
        if (!_byFile.TryGetValue(filePath, out var occ)) return null;
        foreach (var o in occ)
            if (offset >= o.Offset && offset <= o.Offset + o.Length) return o;
        return null;
    }

    /// <summary>返回某种类下所有已定义的符号名（去重），供补全候选。</summary>
    public IReadOnlyCollection<string> GetDefinedNames(SymbolKind kind)
    {
        var set = new HashSet<string>();
        foreach (var kvp in _definitions)
            if (kvp.Key.Kind == kind) set.Add(kvp.Value.Name);
        return set;
    }

    /// <summary>对某文件做诊断：未定义引用 + 重复定义。</summary>
    public IReadOnlyList<Diagnostic> GetDiagnostics(string filePath)
    {
        var result = new List<Diagnostic>();
        if (!_byFile.TryGetValue(filePath, out var occ)) return result;

        // 重复定义警告——仅「声明式」定义参与（define / scene / character / label / func）。
        // set（赋值）、let/local（块级声明）重复书写不构成重复定义。
        var seenDefs = new HashSet<SymbolKey>();
        foreach (var o in occ)
        {
            if (o.Role != SymbolRole.Definition) continue;
            if (!o.IsDeclaration) continue;
            if (!seenDefs.Add(o.Key))
                result.Add(new Diagnostic(DiagnosticSeverity.Warning,
                    $"重复定义{o.Kind}「{o.Name}」", new Location(filePath, o.Offset, o.Length)));
        }

        // 未定义引用错误——点分属性路径（player.name / npc.innkeeper.name 等）多为 C# 运行时注入的对象属性，
        // 引擎在运行时动态解析，.story 静态索引无法枚举，跳过未定义告警以免误报。
        foreach (var o in occ)
        {
            if (o.Role != SymbolRole.Reference) continue;
            if (o.Name.Contains('.')) continue;
            var fb = o.Kind == SymbolKind.Label ? SymbolKind.Scene : (SymbolKind?)null;
            if (Resolve(o.Kind, fb, o.Name) is null)
                result.Add(new Diagnostic(DiagnosticSeverity.Error,
                    $"未定义的{o.Kind}「{o.Name}」", new Location(filePath, o.Offset, o.Length)));
        }

        return result;
    }

    // ===== 内部 =====

    private void AddOccurrence(SymbolOccurrence o)
    {
        if (o.Role == SymbolRole.Definition)
        {
            if (!_definitions.ContainsKey(o.Key)) _definitions[o.Key] = o;
        }
        else
        {
            if (!_references.TryGetValue(o.Key, out var list))
            {
                list = new List<SymbolOccurrence>();
                _references[o.Key] = list;
            }
            list.Add(o);
        }
    }

    private void RemoveOccurrence(SymbolOccurrence o)
    {
        if (o.Role == SymbolRole.Definition)
        {
            if (_definitions.TryGetValue(o.Key, out var existing) && existing.FilePath == o.FilePath && existing.Offset == o.Offset)
                _definitions.Remove(o.Key);
        }
        else if (_references.TryGetValue(o.Key, out var list))
        {
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].FilePath == o.FilePath && list[i].Offset == o.Offset)
                {
                    list.RemoveAt(i);
                    break;
                }
            }
            if (list.Count == 0) _references.Remove(o.Key);
        }
    }

    private static List<SymbolOccurrence> CollectOccurrences(string filePath, DslToken[] tokens, ReadOnlySpan<char> source)
    {
        var occurrences = new List<SymbolOccurrence>();
        if (tokens.Length == 0) return occurrences;

        // 计算每行首字符偏移，用于把扁平 token 流按行分组（token 不跨行）
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') lineStarts.Add(i + 1);

        var i2 = 0;
        while (i2 < tokens.Length)
        {
            var lineIdx = LineOf(lineStarts, tokens[i2].Offset);
            // 该行结束偏移（不含 '\n'）= 下一行首 - 1；末行则到 source 末尾
            var lineEnd = (lineIdx + 1 < lineStarts.Count) ? lineStarts[lineIdx + 1] - 1 : source.Length;

            var start = i2;
            while (i2 < tokens.Length && tokens[i2].Offset < lineEnd) i2++;
            var count = i2 - start;
            var lineTokens = new DslToken[count];
            for (var k = 0; k < count; k++) lineTokens[k] = tokens[start + k];

            ProcessLine(filePath, lineTokens, source, occurrences);
        }

        return occurrences;
    }

    /// <summary>对若干行（已分组、绝对偏移 token）收集符号出现，供增量重索引复用。</summary>
    private static List<SymbolOccurrence> CollectOccurrencesForLines(string filePath, DslToken[][] lines, ReadOnlySpan<char> source)
    {
        var occurrences = new List<SymbolOccurrence>();
        foreach (var line in lines) ProcessLine(filePath, line, source, occurrences);
        return occurrences;
    }

    /// <summary>在按 Offset 升序的列表中二分定位首个 Offset >= target 的下标。</summary>
    private static int LowerBound(List<SymbolOccurrence> list, int target)
    {
        var lo = 0;
        var hi = list.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (list[mid].Offset < target) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int LineOf(List<int> lineStarts, int offset)
    {
        // lineStarts 单调递增，二分定位 offset 所属行
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

    private static void ProcessLine(string filePath, DslToken[] lineTokens, ReadOnlySpan<char> source, List<SymbolOccurrence> occurrences)
    {
        if (lineTokens.Length == 0) return;
        var head = lineTokens[0];
        if (head.Kind != DslTokenKind.Keyword) return;
        var headText = head.GetText(source).ToString();

        switch (headText)
        {
            case "scene":
                if (TryNextString(lineTokens, 1, source, out var sceneName))
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Scene, SymbolRole.Definition, sceneName, filePath, lineTokens[1].Offset, lineTokens[1].Length, SymbolScope.Global, true));
                break;
            case "character":
                if (TryNextString(lineTokens, 1, source, out var charName))
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Character, SymbolRole.Definition, charName, filePath, lineTokens[1].Offset, lineTokens[1].Length, SymbolScope.Global, true));
                break;
            case "label":
                if (TryNextIdentifier(lineTokens, 1, source, out var labelName))
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Label, SymbolRole.Definition, labelName, filePath, lineTokens[1].Offset, lineTokens[1].Length, SymbolScope.Scene, true));
                break;
            case "set":
            case "define":
            case "let":
            case "local":
                // 变量名既可能是裸标识符（set sex 1），也可能是带引号字符串（define "npc.innkeeper.name" "老张" once）。
                // 裸标识符优先，否则取引号内字符串作为变量名——两者按同一符号名收集，引用端 {name} 才能匹配解析。
                // 生命周期：define/set 为全局（define 尤甚，无论写在哪个 scene/文件都恒为全局）；
                // let/local 为局部（场景/块级），仅影响语义展示，不影响跨文件解析（解析保持「任一作用域有定义即命中」）。
                // 声明式：只有 define 才算「声明」（参与重复定义检测）；set 是赋值、let/local 是块级声明，
                // 重复书写/重复赋值不构成重复定义。
                if (TryNextIdentifier(lineTokens, 1, source, out var varName) || TryNextString(lineTokens, 1, source, out varName))
                {
                    var scope = headText is "let" or "local" ? SymbolScope.Local : SymbolScope.Global;
                    var isDecl = headText == "define";
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Variable, SymbolRole.Definition, varName, filePath, lineTokens[1].Offset, lineTokens[1].Length, scope, isDecl));
                }
                break;
            case "func":
                if (TryNextIdentifier(lineTokens, 1, source, out var funcName))
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Func, SymbolRole.Definition, funcName, filePath, lineTokens[1].Offset, lineTokens[1].Length, SymbolScope.Global, true));
                break;
            case "jump":
                if (TryNextIdentifier(lineTokens, 1, source, out var jumpTarget))
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Label, SymbolRole.Reference, jumpTarget, filePath, lineTokens[1].Offset, lineTokens[1].Length));
                break;
            case "navigate":
                if (TryNextString(lineTokens, 1, source, out var navTarget))
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Scene, SymbolRole.Reference, navTarget, filePath, lineTokens[1].Offset, lineTokens[1].Length));
                break;
            case "call":
                if (TryNextIdentifier(lineTokens, 1, source, out var callTarget))
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Func, SymbolRole.Reference, callTarget, filePath, lineTokens[1].Offset, lineTokens[1].Length));
                break;
            case "menu":
                for (var k = 0; k + 1 < lineTokens.Length; k++)
                {
                    if (lineTokens[k].Kind == DslTokenKind.Symbol && lineTokens[k].GetText(source).ToString() == "->")
                    {
                        var target = lineTokens[k + 1];
                        if (target.Kind == DslTokenKind.Identifier)
                        {
                            var name = target.GetText(source).ToString();
                            occurrences.Add(new SymbolOccurrence(SymbolKind.Label, SymbolRole.Reference, name, filePath, target.Offset, name.Length));
                        }
                    }
                }
                break;
        }

        // 所有字符串内的 {var} / {var:type} 插值 -> 变量引用
        foreach (var t in lineTokens)
        {
            if (t.Kind != DslTokenKind.String) continue;
            var text = t.GetText(source).ToString();
            var inner = Unquote(text);
            CollectInterpolations(inner, t.Offset + 1, occurrences, filePath);
        }
    }

    private static void CollectInterpolations(string text, int contentOffset, List<SymbolOccurrence> occurrences, string filePath)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{')
            {
                i++;
                continue;
            }
            var end = text.IndexOf('}', i + 1);
            if (end < 0) break;
            var expr = text.Substring(i + 1, end - i - 1).Trim();
            // 行内富文本标记（{b}{/b}{i}{/i}{w}{fast}{p} 及 color=/font=/size= 前缀）不是变量引用，
            // 用 DslInlineTags 单一真相源判定，避免把 {b} 误报成「未定义的变量」。
            if (DslInlineTags.IsInlineTag(expr)) { i = end + 1; continue; }
            // 剥离可选的类型注解 :type
            var colon = expr.IndexOf(':');
            var name = colon >= 0 ? expr.Substring(0, colon).Trim() : expr;
            if (IsVariableName(name))
                occurrences.Add(new SymbolOccurrence(SymbolKind.Variable, SymbolRole.Reference, name, filePath, contentOffset + i + 1, name.Length));
            i = end + 1;
        }
    }

    private static bool TryNextString(DslToken[] line, int index, ReadOnlySpan<char> source, out string value)
    {
        value = string.Empty;
        if (index >= line.Length || line[index].Kind != DslTokenKind.String) return false;
        value = Unquote(line[index].GetText(source).ToString());
        return true;
    }

    private static bool TryNextIdentifier(DslToken[] line, int index, ReadOnlySpan<char> source, out string value)
    {
        value = string.Empty;
        if (index >= line.Length || line[index].Kind != DslTokenKind.Identifier) return false;
        value = line[index].GetText(source).ToString();
        return true;
    }

    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            return text.Substring(1, text.Length - 2);
        if (text.Length >= 1 && text[0] == '"')
            return text.Substring(1);
        return text;
    }

    /// <summary>判定 {...} 内插值主体是否为合法变量/属性路径名。</summary>
    /// <remarks>
    /// 行内富文本标记已由 <see cref="DslInlineTags.IsInlineTag"/> 在收集点排除，此处只校验变量命名：
    /// 首字符为字母/下划线，后续允许字母/数字/下划线/点号（属性路径，如 player.name），
    /// 且不能以点号开头或结尾。{sex} 与 {player.name} 都算合法变量引用。
    /// </remarks>
    private static bool IsVariableName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        foreach (var c in s)
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.') return false;
        return s[0] != '.' && s[s.Length - 1] != '.';
    }
}
