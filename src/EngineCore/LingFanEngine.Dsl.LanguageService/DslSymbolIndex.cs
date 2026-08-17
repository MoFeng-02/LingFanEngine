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
            // 必须原样保留 IsDeclaration：7 参构造默认 isDeclaration=false，会让声明式定义（label/scene/character/func/style/define）
            // 在尾部平移后从 _definitions 消失，导致前向 jump/navigate 误报未定义、且补全候选（GetDefinedNames）变空。
            // 这是 B40：编辑「声明上方」任意行都会触发尾部平移，故声明一旦被编辑上方改动就丢失。
            list[i] = new SymbolOccurrence(o.Kind, o.Role, o.Name, o.FilePath, o.Offset + delta, o.Length, o.Scope, o.IsDeclaration);
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

    /// <summary>内部/临时变量名约定：以下划线 `_` 开头（含 `__` 引擎保留变量与 `_local_` 用户临时变量）。
    /// 这类变量由 set 自动创建、无需 define 声明，静态分析豁免「未定义」检查，避免误报。</summary>
    public static bool IsInternalVariableName(string name) => name.StartsWith("_");

    /// <summary>返回某种类下所有已定义的符号名（去重），供补全候选。</summary>
    public IReadOnlyCollection<string> GetDefinedNames(SymbolKind kind)
    {
        var set = new HashSet<string>();
        foreach (var kvp in _definitions)
            if (kvp.Key.Kind == kind) set.Add(kvp.Value.Name);
        return set;
    }

    /// <summary>返回所有变量名及其作用域（B32）：define→全局、let/local→局部、仅 set（create-or-set）→全局。
    /// 跨文件合并时 define 优先为全局；其余按「出现即局部」计入局部。供补全候选标注作用域徽标。</summary>
    public IReadOnlyDictionary<string, SymbolScope> GetVariablesWithScope()
    {
        var scopes = new Dictionary<string, SymbolScope>(StringComparer.Ordinal);
        foreach (var kvp in _byFile)
        {
            foreach (var o in kvp.Value)
            {
                if (o.Kind != SymbolKind.Variable || o.Role != SymbolRole.Definition) continue;
                if (o.IsDeclaration) scopes[o.Name] = SymbolScope.Global;                 // define 全局，覆盖一切
                else if (o.Scope == SymbolScope.Local)
                {
                    if (!scopes.TryGetValue(o.Name, out var cur) || cur != SymbolScope.Global)
                        scopes[o.Name] = SymbolScope.Local;                              // let/local → 局部
                }
                else scopes.TryAdd(o.Name, SymbolScope.Global);                          // 仅 set → 全局
            }
        }
        return scopes;
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

        // 未定义引用错误。
        // 关于点分名字（player.name / npc.innkeeper.trust / story.good_deeds 等）：本 DSL 中点号只是「命名空间约定」，
        // 它们与裸标识符一样是 define / set 以字符串键声明的扁平变量（见 LingFanDslEngineTests：define "player.hp" → key 即 "player.hp"），
        // 并非 C# 运行时注入的对象属性。因此必须纳入未定义检查——否则 npc.innkeeper.name 这类被移走或拼错的变量永远不会爆红。
        // 历史上为绕过 B31「扫描 0 文件导致 player.name 误报」而加的「点分一律跳过」是临时补丁，索引正常后反而成 bug，现移除。
        // 仅跳过引擎内部保留变量（双下划线前缀 __，如 __for_idx / __for_len）：它们由 for/switch 编译生成，无需在故事里声明。
        foreach (var o in occ)
        {
            if (o.Role != SymbolRole.Reference) continue;
            // 内部/临时变量（_ 前缀，含 __ 引擎保留变量与 _local_ 用户临时变量）由 set 自动创建，
            // 无需 define 声明，豁免「未定义」检查，避免把 _local_wolf_hp 这类合法临时变量误报成未定义。
            if (IsInternalVariableName(o.Name)) continue;
            // 点分变量（player.level / npc.innkeeper.trust 等）与裸标识符一样，是经 define / set 以字符串键声明的扁平变量，
            // 并非引擎运行时注入的对象属性——故一律纳入未定义检查：拼写错、被移走、或从未声明的变量都会正确爆红。
            var fb = o.Kind switch
            {
                SymbolKind.Label => SymbolKind.Scene,
                SymbolKind.Scene => SymbolKind.Label,
                SymbolKind.Func => SymbolKind.Label,
                _ => (SymbolKind?)null,
            };
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
            // 「已定义」表只收录「声明式」定义（define / let / local，IsDeclaration=true）。
            // set（赋值，IsDeclaration=false）是写引用，绝不进入「已定义」表——
            // 否则「把 define 移走、只留 set」时变量仍会被判为「已定义」，导致未定义引用永不爆红（B37 根因）。
            // 这也意味着：若某变量只剩 set 而无 define，它的所有读取引用（{x} / 表达式 RHS）都会被正确判为未定义并爆红。
            if (o.IsDeclaration)
                _definitions[o.Key] = o;
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

            var lineStart = lineStarts[lineIdx];
            var lineText = source.Slice(lineStart, lineEnd - lineStart).ToString();
            ProcessLine(filePath, lineTokens, source, lineText, lineStart, occurrences);
        }

        // 作用域修正（B32）：set 赋值的作用域应跟随其目标变量的「声明作用域」，而非一律 Global。
        // 先在本文件声明中推导各变量作用域（define→全局、let/local→局部、仅 set→全局），
        // 再把 set 出现的作用域对齐到该声明作用域，使索引忠实反映 define/set/let/local 的区别。
        var declScope = new Dictionary<string, SymbolScope>(StringComparer.Ordinal);
        foreach (var o in occurrences)
        {
            if (o.Kind != SymbolKind.Variable || o.Role != SymbolRole.Definition) continue;
            if (o.IsDeclaration) declScope[o.Name] = SymbolScope.Global;          // define 全局，覆盖一切
            else if (o.Scope == SymbolScope.Local) declScope[o.Name] = SymbolScope.Local; // let/local → 局部（覆盖仅 set 的全局）
            else declScope.TryAdd(o.Name, SymbolScope.Global);                    // 仅 set → 全局（仅当尚未设定）
        }
        for (var i = 0; i < occurrences.Count; i++)
        {
            var o = occurrences[i];
            if (o.Kind == SymbolKind.Variable && o.Role == SymbolRole.Definition && !o.IsDeclaration
                && declScope.TryGetValue(o.Name, out var sc) && sc != o.Scope)
            {
                occurrences[i] = new SymbolOccurrence(o.Kind, o.Role, o.Name, o.FilePath, o.Offset, o.Length, sc, o.IsDeclaration);
            }
        }

        return occurrences;
    }

    /// <summary>对若干行（已分组、绝对偏移 token）收集符号出现，供增量重索引复用。</summary>
    private static List<SymbolOccurrence> CollectOccurrencesForLines(string filePath, DslToken[][] lines, ReadOnlySpan<char> source)
    {
        var occurrences = new List<SymbolOccurrence>();
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            var lineStart = line[0].Offset;
            var lineEnd = line[line.Length - 1].Offset + line[line.Length - 1].Length;
            var lineText = source.Slice(lineStart, lineEnd - lineStart).ToString();
            ProcessLine(filePath, line, source, lineText, lineStart, occurrences);
        }
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

    private static readonly HashSet<string> s_exprBuiltins = new(StringComparer.Ordinal)
    {
        "random", "min", "max", "abs", "clamp", "true", "false"
    };

    private static void ProcessLine(string filePath, DslToken[] lineTokens, ReadOnlySpan<char> source, string lineText, int lineStart, List<SymbolOccurrence> occurrences)
    {
        if (lineTokens.Length > 0 && lineTokens[0].Kind == DslTokenKind.Keyword)
        {
            var headText = lineTokens[0].GetText(source).ToString();
            switch (headText)
            {
                case "scene":
                    if (TryNextString(lineTokens, 1, source, out var sceneName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Scene, SymbolRole.Definition, sceneName, filePath, s.Offset, s.Length, SymbolScope.Global, true)); }
                    break;
                case "character":
                    if (TryNextString(lineTokens, 1, source, out var charName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Character, SymbolRole.Definition, charName, filePath, s.Offset, s.Length, SymbolScope.Global, true)); }
                    break;
                case "label":
                    if (TryNextIdentifier(lineTokens, 1, source, out var labelName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Label, SymbolRole.Definition, labelName, filePath, s.Offset, s.Length, SymbolScope.Scene, true)); }
                    break;
                case "set":
                case "define":
                case "let":
                case "local":
                    // 变量名既可能是裸标识符（set sex 1），也可能是带引号字符串（define "npc.innkeeper.name" "老张" once）。
                    if (TryNextIdentifier(lineTokens, 1, source, out var varName) || TryNextString(lineTokens, 1, source, out varName))
                    {
                        var s = NameSpan(lineTokens[1], source);
                        var scope = headText is "let" or "local" ? SymbolScope.Local : SymbolScope.Global;
                        var isDecl = headText == "define";
                        occurrences.Add(new SymbolOccurrence(SymbolKind.Variable, SymbolRole.Definition, varName, filePath, s.Offset, s.Length, scope, isDecl));
                    }
                    break;
                case "func":
                    if (TryNextIdentifier(lineTokens, 1, source, out var funcName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Func, SymbolRole.Definition, funcName, filePath, s.Offset, s.Length, SymbolScope.Global, true)); }
                    break;
                case "style":
                    if (TryNextString(lineTokens, 1, source, out var styleName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Style, SymbolRole.Definition, styleName, filePath, s.Offset, s.Length, SymbolScope.Global, true)); }
                    break;
                case "jump":
                    if (TryNextIdentifier(lineTokens, 1, source, out var jumpTarget))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Label, SymbolRole.Reference, jumpTarget, filePath, s.Offset, s.Length)); }
                    break;
                case "navigate":
                    if (TryNextString(lineTokens, 1, source, out var navTarget))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Scene, SymbolRole.Reference, navTarget, filePath, s.Offset, s.Length)); }
                    break;
                case "call":
                    if (TryNextIdentifier(lineTokens, 1, source, out var callTarget))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Func, SymbolRole.Reference, callTarget, filePath, s.Offset, s.Length)); }
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
        }

        // 注释区间——避免把注释里的 {x} 误当插值引用索引
        var commentSpans = new List<(int, int)>(lineTokens.Length);
        foreach (var t in lineTokens)
            if (t.Kind == DslTokenKind.Comment) commentSpans.Add((t.Offset, t.Offset + t.Length));

        // 全行扫描 {...} 插值：覆盖「字符串内的插值」与「裸花括号表达式（if/while 条件、set 值等）」两类上下文。
        ScanInterpolations(lineText, lineStart, occurrences, filePath, commentSpans);
    }

    /// <summary>全行扫描 {...} 插值，提取其中的变量引用（表达式里的多个标识符一并收集，如 {a + b.c}）。</summary>
    private static void ScanInterpolations(string text, int lineStart, List<SymbolOccurrence> occurrences, string filePath, List<(int, int)> commentSpans)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '{') { i++; continue; }
            var end = text.IndexOf('}', i + 1);
            if (end < 0) break;
            var absOpen = lineStart + i;
            var inComment = false;
            foreach (var (cs, ce) in commentSpans)
            {
                if (absOpen >= cs && absOpen < ce) { inComment = true; break; }
            }
            if (inComment) { i = end + 1; continue; }

            var expr = text.Substring(i + 1, end - i - 1).Trim();
            // 行内富文本标记（{b}{/b}{i}{/i}{color=…}{/color}{size=…} 等）不是变量引用，
            // 用 DslCore.DslInlineTags 单一真相源判定，避免把 {b}/{color=…} 误报成「未定义的变量」。
            // B33 重写时此判定被遗漏，导致严重的误报回归，此处恢复。
            if (DslInlineTags.IsInlineTag(expr)) { i = end + 1; continue; }
            CollectExprReferences(expr, absOpen + 1, occurrences, filePath);
            i = end + 1;
        }
    }

    /// <summary>从插值表达式（{...} 内文）提取所有变量引用。支持 {a + b.c * 2}、格式注解 {name:color}（已剥离）、三元 {x ? 1 : 2}。</summary>
    private static void CollectExprReferences(string rawExpr, int exprOffset, List<SymbolOccurrence> occurrences, string filePath)
    {
        // 剥离末尾的格式注解 :format（仅当 : 之后全是字母数字且无空白，形如 {name:color} / {x + 1:red}）
        var work = rawExpr;
        var lastColon = work.LastIndexOf(':');
        if (lastColon >= 0)
        {
            var after = work.Substring(lastColon + 1);
            if (after.Length > 0)
            {
                var ok = true;
                foreach (var c in after)
                {
                    if (char.IsWhiteSpace(c) || !(char.IsLetterOrDigit(c) || c == '_')) { ok = false; break; }
                }
                if (ok) work = work.Substring(0, lastColon);
            }
        }

        var i = 0;
        while (i < work.Length)
        {
            var c = work[i];
            if (char.IsLetter(c) || c == '_')
            {
                var j = i;
                while (j < work.Length && (char.IsLetterOrDigit(work[j]) || work[j] == '_' || work[j] == '.')) j++;
                var name = work.Substring(i, j - i);
                if (IsVariableName(name) && !s_exprBuiltins.Contains(name))
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Variable, SymbolRole.Reference, name, filePath, exprOffset + i, name.Length));
                i = j;
            }
            else i++;
        }
    }

    /// <summary>取符号名在源中的精确区间：字符串字面量剥掉引号取内部，裸标识符取本体。</summary>
    private static (int Offset, int Length) NameSpan(DslToken token, ReadOnlySpan<char> source)
    {
        var text = token.GetText(source).ToString();
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            return (token.Offset + 1, text.Length - 2);
        return (token.Offset, token.Length);
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
