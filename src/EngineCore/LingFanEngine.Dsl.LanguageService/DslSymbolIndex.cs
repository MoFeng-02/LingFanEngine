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
    // 局部变量(let/local)定义表：按 文件 -> 变量名 -> 作用域声明列表 三级索引。
    // let/local 是「场景/标签级局部」——仅在声明它的同一文件、同一作用域内可解析（不跨文件、不跨兄弟作用域），
    // 与运行时 LocalScope 的「label→scene→file」回退一致；跨文件 / 跨兄弟作用域的 let 引用必须判为「未定义」，
    // 否则 fileA 的 let 会被误当成 fileB 的引用目标、或 sceneB 的 {x} 误当成 sceneA 的 let 引用目标。
    // 同名变量可在不同作用域各自声明（如 sceneA 与 sceneB 各 let "x"），故值用列表而非单值。
    private readonly Dictionary<string, Dictionary<string, List<LocalVarDecl>>> _localVarDefsByFile = new();

    /// <summary>局部变量声明 + 其所在作用域路径（"scene/名" / "label/名" / "" 文件级）。</summary>
    private readonly struct LocalVarDecl
    {
        public readonly string ScopePath;
        public readonly SymbolOccurrence Occ;
        public LocalVarDecl(string scopePath, SymbolOccurrence occ) { ScopePath = scopePath ?? ""; Occ = occ; }
    }

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
            list[i] = new SymbolOccurrence(o.Kind, o.Role, o.Name, o.FilePath, o.Offset + delta, o.Length, o.Scope, o.IsDeclaration, o.ScopePath, o.IsOptional);
            AddOccurrence(list[i]);
        }

        // 2) 受影响行增量重索引（仅这几行，无全文 lineStarts 重扫）
        //    先据「受影响区域之前」最近 scene/label 边界预置作用域，使增量重索引的行获得正确的作用域路径（见 UpdateScope）。
        ComputeScopeAt(list, affectedStartOld, out var curScopeKind, out var curScopeName);
        var newOcc = CollectOccurrencesForLines(filePath, affectedLines, source, ref curScopeKind, ref curScopeName);
        foreach (var o in newOcc) AddOccurrence(o);
        if (newOcc.Count > 0) list.InsertRange(lo, newOcc);
    }

    /// <summary>在受影响区域之前定位「最近前置 scene/label 边界」，作为增量重索引的起始作用域（保证局部变量作用域路径正确）。</summary>
    private static void ComputeScopeAt(List<SymbolOccurrence> list, int beforeOffset, out string kind, out string name)
    {
        kind = ""; name = "";
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var o = list[i];
            if (o.Offset >= beforeOffset) continue;
            if (o.Role == SymbolRole.Definition && (o.Kind == SymbolKind.Label || o.Kind == SymbolKind.Scene))
            {
                kind = o.Kind == SymbolKind.Scene ? "scene" : "label";
                name = o.Name;
                return;
            }
        }
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

    /// <summary>解析定义位置（用于跳转定义）。支持 fallback 种类（如 jump 可指向 label 也可指向 scene）。
    /// <param name="fromFile">引用所在文件。局部变量(let/local)仅在此文件内解析——不跨文件、不跨作用域；
    /// 传 null 则退化为全局解析（仅用于不需要文件上下文的内部回溯）。</param></summary>
    public SymbolOccurrence? Resolve(SymbolKind primary, SymbolKind? fallback, string name, string? fromFile = null, string refScopePath = "")
    {
        // 变量解析：仅当 fromFile 与声明同文件时，局部定义(let/local)才优先于全局——与运行时「同文件 _local_ 遮蔽全局」严格一致，
        // 且确保「fileA 的 let」不会被误当成「fileB 的 {temp}」的引用目标（跨文件不解析）。
        // 作用域隔离（对齐引擎 LocalScope）：引用解析先看「与引用同作用域」的声明（最内层），否则回退到「文件级」声明（内层可见外层），
        // 兄弟作用域的声明互不可见 -> 解析失败 -> 诊断判为「未定义」。
        if (primary == SymbolKind.Variable && fromFile != null
            && _localVarDefsByFile.TryGetValue(fromFile, out var fileLocals)
            && fileLocals.TryGetValue(name, out var decls))
        {
            // 1) 最内层：与引用同作用域（scopePath 完全一致）
            foreach (var d in decls)
                if (string.Equals(d.ScopePath, refScopePath, StringComparison.Ordinal)) return d.Occ;
            // 2) 外层：文件级（scopePath 为空）——JS 语义「内层可见外层」
            if (refScopePath.Length != 0)
                foreach (var d in decls)
                    if (d.ScopePath.Length == 0) return d.Occ;
            return null;   // 仅兄弟作用域有声明 -> 跨作用域不可见 -> 未定义
        }
        if (_definitions.TryGetValue(new SymbolKey(primary, name), out var def)) return def;
        if (fallback is { } fb && _definitions.TryGetValue(new SymbolKey(fb, name), out var fbDef)) return fbDef;
        return null;
    }

    /// <summary>收集某符号的所有引用位置（用于查找所有引用）。
    /// <param name="inFile">非 null 时仅返回该文件内的引用——局部变量(let/local)查找引用应限制在同文件，避免把其它文件的同名局部引用混入。</param></summary>
    public IReadOnlyList<Location> FindReferences(SymbolKind kind, string name, string? inFile = null)
    {
        if (!_references.TryGetValue(new SymbolKey(kind, name), out var list))
            return System.Array.Empty<Location>();
        var locations = new List<Location>(list.Count);
        foreach (var o in list)
        {
            if (inFile != null && !string.Equals(o.FilePath, inFile, StringComparison.Ordinal)) continue;
            locations.Add(new Location(o.FilePath, o.Offset, o.Length));
        }
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

    /// <summary>返回某文件内的全部「声明式」定义（scene/character/label/func/style/define，不含块级 let/local），供大纲（documentSymbol）。
    /// 按 token 序（文件序）返回，调用方据 ScopePath 构建场景→标签层级。</summary>
    public IReadOnlyList<SymbolOccurrence> GetDefinitionsInFile(string filePath)
    {
        if (!_byFile.TryGetValue(filePath, out var occ)) return System.Array.Empty<SymbolOccurrence>();
        var defs = new List<SymbolOccurrence>();
        foreach (var o in occ)
            if (o.Role == SymbolRole.Definition && o.IsDeclaration && o.Scope != SymbolScope.Local)
                defs.Add(o);
        return defs;
    }

    /// <summary>返回跨文件全部「声明式」定义（供 workspace/symbol 全局符号搜索）。</summary>
    public IReadOnlyList<SymbolOccurrence> GetAllDefinitions()
    {
        var list = new List<SymbolOccurrence>(_definitions.Count);
        foreach (var kvp in _definitions) list.Add(kvp.Value);
        return list;
    }

    /// <summary>快照某文件当前「作为定义」的全部符号键（用于跨文件诊断定向刷新）。
    /// 仅扫描全局 _definitions 中 FilePath 命中该文件的条目，O(定义总数)，无反射。</summary>
    public HashSet<SymbolKey> SnapshotDefinitions(string filePath)
    {
        var set = new HashSet<SymbolKey>();
        foreach (var kvp in _definitions)
            if (string.Equals(kvp.Value.FilePath, filePath, StringComparison.Ordinal))
                set.Add(kvp.Key);
        return set;
    }

    /// <summary>重索引某文件后调用：由 editedPath 的定义前后快照求「对称差」，得到定义状态发生变化的符号集合；
    /// 再用现有 _references 索引（符号→引用列表）反查这些符号被哪些文件引用，返回需要重发诊断的全部文件路径
    /// （含 editedPath 自身）。性能损耗 = O(变更符号数 × 引用文件数)，远低于全量重发所有打开文档。</summary>
    public HashSet<string> GetAffectedFiles(string editedPath, HashSet<SymbolKey> before)
    {
        var after = SnapshotDefinitions(editedPath);
        // changed = before ⊕ after（对称差）
        var changed = new HashSet<SymbolKey>(before);
        foreach (var k in after) changed.Add(k);
        var intersection = new HashSet<SymbolKey>(before);
        intersection.IntersectWith(after);
        changed.ExceptWith(intersection);

        var affected = new HashSet<string>(StringComparer.Ordinal) { editedPath };
        foreach (var k in changed)
            if (_references.TryGetValue(k, out var refs))
                foreach (var r in refs)
                {
                    // 局部变量(let/local)不跨文件解析：其定义变更只影响同文件引用，忽略其它文件的同名局部引用，避免误重发无关文件诊断。
                    if (r.Scope == SymbolScope.Local && !string.Equals(r.FilePath, editedPath, StringComparison.Ordinal)) continue;
                    affected.Add(r.FilePath);
                }
        return affected;
    }

    /// <summary>返回变量名及其作用域信息（B32 升级：含场景/标签级局部）。
    /// 值语义：<c>"全局"</c> = define/set（全局）；否则为作用域路径 <c>""</c>(文件局部) / <c>"scene/名"</c>(场景局部) / <c>"label/名"</c>(标签局部)。
    /// 供补全候选标注作用域徽标（文件局部 / 场景局部 / 标签局部 / 全局）。
    /// <param name="filePath">非 null 时仅扫描该文件——避免把 fileA 的 let 当成 fileB 的局部变量候选（let 文件级局部，不跨文件）。</param></summary>
    public IReadOnlyDictionary<string, string> GetVariablesWithScope(string? filePath = null)
    {
        var scopes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in _byFile)
        {
            if (filePath != null && !string.Equals(kvp.Key, filePath, StringComparison.Ordinal)) continue;
            foreach (var o in kvp.Value)
            {
                if (o.Kind != SymbolKind.Variable || o.Role != SymbolRole.Definition) continue;
                // 作用域以 SymbolScope 为准：let/local 局部（记录作用域路径），define 全局，仅 set（全局作用域）全局。
                if (o.Scope == SymbolScope.Local)
                    scopes[o.Name] = o.ScopePath;                                   // let/local → 场景/标签/文件局部（路径区分）
                else if (o.IsDeclaration)
                    scopes[o.Name] = "全局";                                        // define 全局，覆盖一切
                else scopes.TryAdd(o.Name, "全局");                                // 仅 set（全局作用域）→ 全局
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
            if (o.Scope == SymbolScope.Local) continue;   // let/local 块级局部变量，重复声明属正常写法
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
            // 可选引用（如 say 说话人）：解析不到目标定义不算错误——只是普通说话人标记，不报未定义诊断。
            if (o.IsOptional) continue;
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
            if (Resolve(o.Kind, fb, o.Name, o.FilePath, o.ScopePath) is null)
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
            // 注意：let/local（局部作用域）声明【不】进入全局 _definitions——否则其跨文件可见，会经 Resolve 的全局 fallback
            // 被其它文件的 {temp} 误当成引用目标（跨文件/跨作用域泄漏）。局部变量只登记到 _localVarDefsByFile[文件]，
            // 仅同文件内可解析；跨文件引用自然落到「未定义」。
            if (o.IsDeclaration && !(o.Kind == SymbolKind.Variable && o.Scope == SymbolScope.Local))
                _definitions[o.Key] = o;
            // 局部变量(let/local)额外登记到 _localVarDefsByFile[文件]，供变量解析时优先匹配（同文件局部遮蔽全局，准确跳转）。
            // 按 文件 -> 变量名 -> 作用域声明列表 三级索引：同名变量可在不同作用域各自声明（sceneA/sceneB 各 let "x"），
            // 解析时按引用所在作用域精确匹配（见 Resolve），确保「fileA 的 let」不会解析为「fileB 的 {temp}」、sceneB 的 {x} 不会误指向 sceneA 的 let。
            if (o.Kind == SymbolKind.Variable && o.IsDeclaration && o.Scope == SymbolScope.Local)
            {
                if (!_localVarDefsByFile.TryGetValue(o.FilePath, out var map))
                {
                    map = new Dictionary<string, List<LocalVarDecl>>(StringComparer.Ordinal);
                    _localVarDefsByFile[o.FilePath] = map;
                }
                if (!map.TryGetValue(o.Name, out var list))
                {
                    list = new List<LocalVarDecl>();
                    map[o.Name] = list;
                }
                list.Add(new LocalVarDecl(o.ScopePath, o));
            }
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
            // 同步清理 _localVarDefsByFile[文件] 中的局部变量定义（按文件名 + 精确偏移定位该作用域声明）
            if (o.Kind == SymbolKind.Variable && o.Scope == SymbolScope.Local
                && _localVarDefsByFile.TryGetValue(o.FilePath, out var map)
                && map.TryGetValue(o.Name, out var list))
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Occ.FilePath == o.FilePath && list[i].Occ.Offset == o.Offset)
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
                if (list.Count == 0) map.Remove(o.Name);
            }
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

        // 作用域栈（就近边界模型，对齐引擎 LocalScope）：随扫描维护「最近前置 scene/label 边界」。
        // 每行的出现归入当前作用域——scene/label 声明行自身也归入其新作用域（使 scene 内的 let 与 scene 同名）。
        var curScopeKind = "";
        var curScopeName = "";
        var seenSceneNames = new HashSet<string>(StringComparer.Ordinal);
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
            UpdateScope(ref curScopeKind, ref curScopeName, lineTokens, source);
            var scopePath = curScopeKind.Length == 0 ? "" : curScopeKind + "/" + curScopeName;
            ProcessLine(filePath, lineTokens, source, lineText, lineStart, occurrences, scopePath, seenSceneNames);
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
                occurrences[i] = new SymbolOccurrence(o.Kind, o.Role, o.Name, o.FilePath, o.Offset, o.Length, sc, o.IsDeclaration, o.ScopePath, o.IsOptional);
            }
        }

        return occurrences;
    }

    /// <summary>对若干行（已分组、绝对偏移 token）收集符号出现，供增量重索引复用。
    /// <param name="curScopeKind">受影响的起始作用域种类（由调用方据受影响区域之前的最近边界预置）。</param>
    /// <param name="curScopeName">受影响的起始作用域名。</param></summary>
    private static List<SymbolOccurrence> CollectOccurrencesForLines(string filePath, DslToken[][] lines, ReadOnlySpan<char> source, ref string curScopeKind, ref string curScopeName)
    {
        var occurrences = new List<SymbolOccurrence>();
        var seenSceneNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            var lineStart = line[0].Offset;
            var lineEnd = line[line.Length - 1].Offset + line[line.Length - 1].Length;
            var lineText = source.Slice(lineStart, lineEnd - lineStart).ToString();
            UpdateScope(ref curScopeKind, ref curScopeName, line, source);
            var scopePath = curScopeKind.Length == 0 ? "" : curScopeKind + "/" + curScopeName;
            ProcessLine(filePath, line, source, lineText, lineStart, occurrences, scopePath, seenSceneNames);
        }
        return occurrences;
    }

    /// <summary>据某行首词（scene/label 声明）更新「最近前置作用域边界」。非边界行不改变作用域——作用域延续到下一个边界或文件末尾。</summary>
    private static void UpdateScope(ref string curScopeKind, ref string curScopeName, DslToken[] lineTokens, ReadOnlySpan<char> source)
    {
        if (lineTokens.Length == 0 || lineTokens[0].Kind != DslTokenKind.Keyword) return;
        var head = lineTokens[0].GetText(source).ToString();
        if (head == "scene")
        {
            if (TryNextString(lineTokens, 1, source, out var sn)) { curScopeKind = "scene"; curScopeName = sn; }
        }
        else if (head == "label")
        {
            if (TryNextIdentifier(lineTokens, 1, source, out var ln)) { curScopeKind = "label"; curScopeName = ln; }
        }
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

    private static void ProcessLine(string filePath, DslToken[] lineTokens, ReadOnlySpan<char> source, string lineText, int lineStart, List<SymbolOccurrence> occurrences, string scopePath, HashSet<string>? seenSceneNames = null)
    {
        if (lineTokens.Length > 0 && lineTokens[0].Kind == DslTokenKind.Keyword)
        {
            var headText = lineTokens[0].GetText(source).ToString();
            switch (headText)
            {
                case "scene":
                    if (TryNextString(lineTokens, 1, source, out var sceneName))
                    {
                        var s = NameSpan(lineTokens[1], source);
                        // 首次出现 = Definition（用于跳转定义/查找引用），后续出现 = Reference（导航，不算重复定义）。
                        var isFirst = seenSceneNames == null || seenSceneNames.Add(sceneName);
                        var role = isFirst ? SymbolRole.Definition : SymbolRole.Reference;
                        occurrences.Add(new SymbolOccurrence(SymbolKind.Scene, role, sceneName, filePath, s.Offset, s.Length, SymbolScope.Global, isFirst, scopePath));
                    }
                    break;
                case "character":
                    if (TryNextString(lineTokens, 1, source, out var charName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Character, SymbolRole.Definition, charName, filePath, s.Offset, s.Length, SymbolScope.Global, true, scopePath)); }
                    break;
                case "label":
                    if (TryNextIdentifier(lineTokens, 1, source, out var labelName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Label, SymbolRole.Definition, labelName, filePath, s.Offset, s.Length, SymbolScope.Scene, true, scopePath)); }
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
                        // define / let / local 都是「声明」式定义，进入定义表（set 仅赋值，不进入定义表，见 B37）。
                        // let/local 声明局部变量（写入 _local_<name>），必须登记为定义，否则其引用会被误报「未定义」。
                        var isDecl = headText is "define" or "let" or "local";
                        occurrences.Add(new SymbolOccurrence(SymbolKind.Variable, SymbolRole.Definition, varName, filePath, s.Offset, s.Length, scope, isDecl, scopePath));
                    }
                    break;
                case "func":
                    if (TryNextIdentifier(lineTokens, 1, source, out var funcName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Func, SymbolRole.Definition, funcName, filePath, s.Offset, s.Length, SymbolScope.Global, true, scopePath)); }
                    break;
                case "style":
                    if (TryNextString(lineTokens, 1, source, out var styleName))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Style, SymbolRole.Definition, styleName, filePath, s.Offset, s.Length, SymbolScope.Global, true, scopePath)); }
                    break;
                case "jump":
                    if (TryNextIdentifier(lineTokens, 1, source, out var jumpTarget))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Label, SymbolRole.Reference, jumpTarget, filePath, s.Offset, s.Length, SymbolScope.Global, false, scopePath)); }
                    break;
                case "navigate":
                    if (TryNextString(lineTokens, 1, source, out var navTarget))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Scene, SymbolRole.Reference, navTarget, filePath, s.Offset, s.Length, SymbolScope.Global, false, scopePath)); }
                    break;
                case "say":
                    // 说话人 → Character 引用（say "Name": … / say by "Name" / say speaker="Name"）。
                    // 进入索引 → 能解析到 character 定义就跳转/重命名；解析不到（旁白、临时名字等）不报未定义错误（标记 IsOptional，由诊断/悬停特殊处理）。
                    ExtractSpeakerReference(lineTokens, source, filePath, occurrences, scopePath);
                    break;
                case "call":
                    if (TryNextIdentifier(lineTokens, 1, source, out var callTarget))
                    { var s = NameSpan(lineTokens[1], source); occurrences.Add(new SymbolOccurrence(SymbolKind.Func, SymbolRole.Reference, callTarget, filePath, s.Offset, s.Length, SymbolScope.Global, false, scopePath)); }
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
                                occurrences.Add(new SymbolOccurrence(SymbolKind.Label, SymbolRole.Reference, name, filePath, target.Offset, name.Length, SymbolScope.Global, false, scopePath));
                            }
                        }
                    }
                    break;
            }
        }

        // scene 块内 UI 元素属性引用（nav=→Scene、class=/style=→Style）：进入索引 → 跳转定义 + 未定义诊断。
        // 仅当行首词是已知 UI 元素类型（DslGrammar.IsUiElement）时才扫描，避免误伤其它语句：
        // ① navigate 的 scene= 已由上方 switch 单独处理；② say 说话人已由 case "say" 单独处理；
        // ③ source=/cmd=/align= 不属于 SymbolKind，本 DSL 经 CollectReferenceDiagnostics 单独诊断，此处故意跳过。
        if (lineTokens.Length > 0 && lineTokens[0].Kind == DslTokenKind.Keyword)
        {
            var firstName = lineTokens[0].GetText(source).ToString();
            var spec = DslGrammar.TryGet(firstName);
            if (spec is { IsUiElement: true })
            {
                for (var k = 1; k + 1 < lineTokens.Length; k++)
                {
                    if (lineTokens[k].Kind == DslTokenKind.Symbol && source[lineTokens[k].Offset] == '='
                        && lineTokens[k - 1].Kind is DslTokenKind.Identifier or DslTokenKind.Keyword)
                    {
                        var key = lineTokens[k - 1].GetText(source).ToString();
                        var refKind = key switch
                        {
                            "nav" => SymbolKind.Scene,
                            "class" or "style" => SymbolKind.Style,
                            _ => (SymbolKind?)null,
                        };
                        if (refKind is { } kind && lineTokens[k + 1].Kind == DslTokenKind.String)
                        {
                            var valTok = lineTokens[k + 1];
                            var name = Unquote(valTok.GetText(source).ToString());
                            if (name.Length > 0)
                            {
                                var s = NameSpan(valTok, source);
                                occurrences.Add(new SymbolOccurrence(kind, SymbolRole.Reference, name, filePath, s.Offset, s.Length, SymbolScope.Global, false, scopePath));
                            }
                        }
                    }
                }
            }
        }

        // 注释区间——避免把注释里的 {x} 误当插值引用索引
        var commentSpans = new List<(int, int)>(lineTokens.Length);
        foreach (var t in lineTokens)
            if (t.Kind == DslTokenKind.Comment) commentSpans.Add((t.Offset, t.Offset + t.Length));

        // 全行扫描 {...} 插值：覆盖「字符串内的插值」与「裸花括号表达式（if/while 条件、set 值等）」两类上下文。
        ScanInterpolations(lineText, lineStart, occurrences, filePath, commentSpans, scopePath);
    }

    /// <summary>从 <c>say</c> 行提取说话人引用（Character），标记 <see cref="SymbolOccurrence.IsOptional"/>（可选引用）。
    /// 优先级：speaker="X" &gt; by "X" &gt; 首个位置参字符串 "X":。
    /// 说话人能解析到 <c>character</c> 定义就提供跳转/重命名；解析不到（旁白、临时名字等）不报未定义错误。</summary>
    private static void ExtractSpeakerReference(DslToken[] lineTokens, ReadOnlySpan<char> source, string filePath, List<SymbolOccurrence> occurrences, string scopePath)
    {
        string? name = null;
        int off = -1, len = 0;

        // 1) speaker="X"
        for (var k = 1; k + 1 < lineTokens.Length; k++)
        {
            if (lineTokens[k].Kind == DslTokenKind.Symbol && source[lineTokens[k].Offset] == '='
                && lineTokens[k - 1].Kind is DslTokenKind.Identifier or DslTokenKind.Keyword
                && string.Equals(lineTokens[k - 1].GetText(source).ToString(), "speaker", StringComparison.Ordinal)
                && lineTokens[k + 1].Kind == DslTokenKind.String)
            {
                var s = NameSpan(lineTokens[k + 1], source);
                name = Unquote(lineTokens[k + 1].GetText(source).ToString()); off = s.Offset; len = s.Length;
                break;
            }
        }
        // 2) by "X"
        if (name == null)
        {
            for (var k = 1; k + 1 < lineTokens.Length; k++)
            {
                if (lineTokens[k].Kind is DslTokenKind.Identifier or DslTokenKind.Keyword
                    && string.Equals(lineTokens[k].GetText(source).ToString(), "by", StringComparison.Ordinal)
                    && lineTokens[k + 1].Kind == DslTokenKind.String)
                {
                    var s = NameSpan(lineTokens[k + 1], source);
                    name = Unquote(lineTokens[k + 1].GetText(source).ToString()); off = s.Offset; len = s.Length;
                    break;
                }
            }
        }
        // 3) 首个位置参字符串 "X":（say "Alice": Hello）
        if (name == null && lineTokens.Length > 1 && lineTokens[1].Kind == DslTokenKind.String)
        {
            var s = NameSpan(lineTokens[1], source);
            name = Unquote(lineTokens[1].GetText(source).ToString()); off = s.Offset; len = s.Length;
        }

        if (name != null && name.Length > 0)
            // 说话人引用是「可选」的：能解析到 character 定义就提供跳转/重命名，解析不到（旁白、临时名字等）不报未定义错误。
            occurrences.Add(new SymbolOccurrence(SymbolKind.Character, SymbolRole.Reference, name, filePath, off, len, SymbolScope.Global, false, scopePath, true));
    }

    /// <summary>全行扫描 {...} 插值，提取其中的变量引用（表达式里的多个标识符一并收集，如 {a + b.c}）。</summary>
    private static void ScanInterpolations(string text, int lineStart, List<SymbolOccurrence> occurrences, string filePath, List<(int, int)> commentSpans, string scopePath)
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
            CollectExprReferences(expr, absOpen + 1, occurrences, filePath, scopePath);
            i = end + 1;
        }
    }

    /// <summary>从插值表达式（{...} 内文）提取所有变量引用。支持 {a + b.c * 2}、格式注解 {name:color}（已剥离）、三元 {x ? 1 : 2}。</summary>
    private static void CollectExprReferences(string rawExpr, int exprOffset, List<SymbolOccurrence> occurrences, string filePath, string scopePath)
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
                    occurrences.Add(new SymbolOccurrence(SymbolKind.Variable, SymbolRole.Reference, name, filePath, exprOffset + i, name.Length, SymbolScope.Global, false, scopePath));
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
