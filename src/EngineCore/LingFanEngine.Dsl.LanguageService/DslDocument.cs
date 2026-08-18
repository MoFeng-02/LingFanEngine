using System.Collections.Generic;
using LingFanEngine.DslCore;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>文档增量更新结果——供符号索引按行级复用，避免每次更新都全量重建。</summary>
public sealed class DocumentUpdateResult
{
    public bool Incremental;
    public DslToken[][]? AffectedLines;   // 受影响行的绝对偏移 token（仅增量成功时非 null）
    public int AffectedStartOld;          // 受影响首行在旧文本中的绝对起始偏移
    public int OldAffectedEnd;            // 旧受影响区域末尾（不含）的绝对偏移
    public int Delta;                     // 文本长度变化（new - old）
}

/// <summary>
/// 单文档状态——持有源码、按行维护 token 流，支持脏区增量重词法。
/// <para>增量策略（规划 §3.3）：DSL 以行为语句单位，每行可独立 token 化。
/// 一次编辑只影响 [startLine, endLine] 区间，未改行复用旧 token；尾部行因总长度变化只对偏移做统一平移。</para>
/// </summary>
public sealed class DslDocument
{
    public string FilePath { get; }

    private string _source;
    private int[] _lineStarts = System.Array.Empty<int>();         // 每行首字符在全文中的绝对偏移；长度 = 行数
    private int _version = 1;                                      // 内容版本号：每次 Update 改变源码后自增，供折叠/语义缓存做 O(1) 失效判等
    private DslToken[][] _lineTokens = System.Array.Empty<DslToken[]>();
    private DocumentUpdateResult? _lastUpdate;

    public DslDocument(string filePath, string text)
    {
        FilePath = filePath;
        _source = text;
        BuildFromScratch();
    }

    /// <summary>源文本视图。</summary>
    public ReadOnlySpan<char> Source => _source.AsSpan();

    /// <summary>源文本字符串引用（与 <see cref="_source"/> 同一实例）。
    /// 折叠/语义缓存用它做内容失效比对，避免每次 <c>doc.Source.ToString()</c> 把整份源码拷贝成新字符串（大文件 O(n) 分配）。</summary>
    public string Text => _source;

    /// <summary>内容版本号：每次 <see cref="Update"/> 改变源码后自增，供折叠/语义缓存做 O(1) 失效判等（避免 O(n) 全文比对）。</summary>
    public int Version => _version;

    /// <summary>扁平化全部 token（绝对偏移——出口处由相对行首偏移还原）。</summary>
    public DslToken[] GetAllTokens()
    {
        var total = 0;
        for (var l = 0; l < _lineTokens.Length; l++) total += _lineTokens[l].Length;
        var all = new DslToken[total];
        var k = 0;
        for (var l = 0; l < _lineTokens.Length; l++)
        {
            var lineStart = _lineStarts[l];
            var line = _lineTokens[l];
            for (var j = 0; j < line.Length; j++)
                all[k++] = new DslToken(lineStart + line[j].Offset, line[j].Length, line[j].Kind);
        }
        return all;
    }

    /// <summary>行数。</summary>
    public int LineCount => _lineStarts.Length;

    /// <summary>返回包含给定偏移的 token（命中测试，绝对偏移）；无则 null。</summary>
    public DslToken? TokenAt(int offset)
    {
        var line = GetLineIndex(offset);
        var lineStart = _lineStarts[line];
        var rel = offset - lineStart;
        foreach (var t in _lineTokens[line])
        {
            if (rel >= t.Offset && rel < t.Offset + t.Length)
                return new DslToken(lineStart + t.Offset, t.Length, t.Kind);
        }
        return null;
    }

    /// <summary>取某行的 token 数组（绝对偏移——出口处由相对行首偏移还原）。</summary>
    public DslToken[] GetLineTokens(int line)
    {
        if (line < 0 || line >= _lineTokens.Length) return System.Array.Empty<DslToken>();
        var lineStart = _lineStarts[line];
        var rel = _lineTokens[line];
        if (rel.Length == 0) return rel;
        var abs = new DslToken[rel.Length];
        for (var j = 0; j < rel.Length; j++)
            abs[j] = new DslToken(lineStart + rel[j].Offset, rel[j].Length, rel[j].Kind);
        return abs;
    }

    /// <summary>取某行首字符在全文中的绝对偏移。</summary>
    public int GetLineStart(int line) =>
        (line >= 0 && line < _lineStarts.Length) ? _lineStarts[line] : 0;

    /// <summary>给定字符偏移，返回其所属行索引（基于行首偏移二分）。</summary>
    public int GetLineIndex(int offset)
    {
        var lo = 0;
        var hi = _lineStarts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) >> 1;
            if (_lineStarts[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>应用文档变更。无脏区时全量重建；有脏区时做行级增量重词法。返回增量结果供符号索引复用。</summary>
    public DocumentUpdateResult Update(string newText, DirtyRange? dirty)
    {
        _lastUpdate = null;
        _version++;   // 内容即将变更（全量或增量两条路径都会改 _source）→ 版本自增，使依赖旧内容的折叠/语义缓存失效
        if (dirty is null || !TryApplyIncremental(newText, dirty.Value))
        {
            _source = newText;
            BuildFromScratch();
            return new DocumentUpdateResult { Incremental = false };
        }
        return _lastUpdate!;
    }

    private void BuildFromScratch()
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < _source.Length; i++)
            if (_source[i] == '\n') starts.Add(i + 1);
        _lineStarts = starts.ToArray();

        _lineTokens = new DslToken[_lineStarts.Length][];
        for (var l = 0; l < _lineStarts.Length; l++)
        {
            var s = _lineStarts[l];
            var e = (l + 1 < _lineStarts.Length) ? _lineStarts[l + 1] : _source.Length;
            if (e > s && _source[e - 1] == '\n') e--;
            if (e > s && _source[e - 1] == '\r') e--;
            // 相对行首偏移存储：tail 行在增量更新时可原样复用，无需重写
            _lineTokens[l] = DslTokenizer.TokenizeLine(_source.AsSpan(s, e - s), 0);
        }
    }

    private bool TryApplyIncremental(string newText, DirtyRange dirty)
    {
        // 仅当脏区合法（落在旧文本范围内）才走增量；否则交回全量重建
        if (dirty.Start < 0 || dirty.OldLength < 0 || dirty.Start + dirty.OldLength > _source.Length)
            return false;

        var oldSource = _source;
        var delta = newText.Length - oldSource.Length;

        // 旧文本行号：基于现有 _lineStarts 二分（O(log L)），不再全扫。
        var startLine = GetLineIndex(dirty.Start);
        var oldEndLine = GetLineIndex(dirty.Start + dirty.OldLength);

        // 受影响首行在旧文本中的绝对起始偏移（前缀未变，与新增后一致）——旧坐标
        var affectedStartOld = _lineStarts[startLine];
        // 旧受影响区域末尾（不含）的绝对偏移——旧坐标；用于符号索引定位待清理片段
        var oldLineCount = _lineStarts.Length;
        var oldAffectedEnd = (oldEndLine + 1 < oldLineCount) ? _lineStarts[oldEndLine + 1] : oldSource.Length;

        // 局部扫描新文本：仅覆盖 [affectedStartOld, 含 changeEnd 的行尾) 这一小段，
        // 计算受影响各行的起始偏移——O(变更大小)，非 O(全文)。
        var changeEnd = dirty.Start + dirty.NewLength;
        var spanEnd = changeEnd;
        while (spanEnd < newText.Length && newText[spanEnd] != '\n') spanEnd++;

        var affectedStarts = new List<int>();
        var pos = affectedStartOld;
        affectedStarts.Add(pos);
        while (pos < spanEnd)
        {
            if (newText[pos] == '\n') { pos++; affectedStarts.Add(pos); }
            else pos++;
        }
        var affectedCount = affectedStarts.Count;
        var newEndLine = startLine + affectedCount - 1;

        // 总行数 = 前缀 + 受影响 + 尾部（旧文本 oldEndLine 之后的行，仅行首偏移 +delta 平移）
        var tailOldStart = oldEndLine + 1;
        var tailCount = tailOldStart < oldLineCount ? oldLineCount - tailOldStart : 0;
        var newLineCount = startLine + affectedCount + tailCount;

        var newLineStarts = new int[newLineCount];
        var newLineTokens = new DslToken[newLineCount][];
        var affectedLines = new DslToken[affectedCount][];   // 受影响行的绝对偏移 token——供符号索引增量复用

        // 1) 前缀行：内容与偏移均未变，原样复用
        for (var l = 0; l < startLine; l++)
        {
            newLineStarts[l] = _lineStarts[l];
            newLineTokens[l] = _lineTokens[l];
        }

        // 2) 受影响行：重新词法化（相对行首偏移 baseOffset=0，存储紧凑）；
        //    同时还原绝对偏移交给符号索引——仅这几行，不再全量 GetAllTokens。
        for (var k = 0; k < affectedCount; k++)
        {
            var l = startLine + k;
            var s = affectedStarts[k];
            var e = (k + 1 < affectedCount) ? affectedStarts[k + 1] : spanEnd;
            var ls = s; var le = e;
            if (le > ls && newText[le - 1] == '\n') le--;
            if (le > ls && newText[le - 1] == '\r') le--;
            newLineStarts[l] = s;
            var rel = DslTokenizer.TokenizeLine(newText.AsSpan(ls, le - ls), 0);
            newLineTokens[l] = rel;
            var abs = new DslToken[rel.Length];
            for (var j = 0; j < rel.Length; j++)
                abs[j] = new DslToken(s + rel[j].Offset, rel[j].Length, rel[j].Kind);
            affectedLines[k] = abs;
        }

        // 3) 尾部行：旧 token 数组（相对偏移）原样复用，仅行首偏移 +delta——零 token 重写
        for (var k = 0; k < tailCount; k++)
        {
            var oldLine = tailOldStart + k;
            var l = startLine + affectedCount + k;
            newLineStarts[l] = _lineStarts[oldLine] + delta;
            newLineTokens[l] = _lineTokens[oldLine];
        }

        _source = newText;
        _lineStarts = newLineStarts;
        _lineTokens = newLineTokens;
        _lastUpdate = new DocumentUpdateResult
        {
            Incremental = true,
            AffectedLines = affectedLines,
            AffectedStartOld = affectedStartOld,
            OldAffectedEnd = oldAffectedEnd,
            Delta = delta
        };
        return true;
    }
}
