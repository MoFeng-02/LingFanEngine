using System.Text;
using LingFanEngine.DslCore;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// DSL 纯缩进规整器（保守格式化）。
/// <para>设计红线：① 只重排缩进 + 去除行尾空白；② 原样保留每行内容与行内注释，绝不丢信息；
/// ③ 块结束判定与引擎 CompileBody 语义对齐——普通体行缩进恰好回落到块缩进且块已有体行时弹栈
/// （块后语句不吞进块体）；缩进浅于块或块尚无体行时视为缩进错误、修复进体（不破坏域）。
/// 这样格式化输出与引擎按缩进判块的行为一致，不会静默改变程序结构。</para>
/// <para>为什么不用 AST 重建：解析器逐行消费、不保留 trivia，而 DSL 支持整行 <c>//</c>/<c>#</c> 与行内注释
/// （<see cref="LingFanEngine"/> 加载链路 <c>StripInlineComment</c>），AST 重建会丢注释。故采用缩进栈算法。</para>
/// <para>续行语义：仅 <c>else</c>/<c>else if</c>（首词取 "else"）对齐到所属 <c>if</c> 同级（比体浅一层）、
/// 不压栈不弹栈、<c>depth = stack.Count - 1</c>。花括号续行块（<c>} else {</c>）压栈使体 +1（由闭合 <c>}</c> 弹出）。
/// <c>case</c>/<c>default</c> 与 <c>scene</c>/<c>if</c> 等同属开块（见
/// <see cref="DslBlockStructure.IsIndentationBlockStarter"/>），压栈使体更深。此规则经真实脚本逐行验证。</para>
/// </summary>
internal static class DslFormatter
{
    /// <summary>规范缩进单位（空格数）。LSP 客户端可在 FormattingOptions 中覆写 tabSize。</summary>
    private const int DefaultIndentUnit = 2;

    /// <summary>缩进栈条目：记录开块关键字、其原始缩进、以及是否已出现真实体行（缩进深于块的行），
    /// 用于区分「块结束」与「体行缩进错误」。</summary>
    private readonly record struct StackEntry(int Indent, string Keyword, bool HasBody);

    /// <summary>格式化整篇文档，返回重排后的完整文本（保留原换行风格）。</summary>
    public static string Format(string source, int? tabSize = null, bool insertSpaces = true)
    {
        var unit = NormalizeUnit(tabSize);
        var lines = SplitLines(source, out var newline);
        var outLines = new string[lines.Length];
        var stack = new Stack<StackEntry>();
        for (var i = 0; i < lines.Length; i++)
            outLines[i] = ProcessLine(lines[i], stack, unit, insertSpaces);
        return string.Join(newline, outLines);
    }

    /// <summary>
    /// 格式化指定行区间 [startLine, endLine]（闭区间，0-based）；范围外行原样保留。
    /// <para>嵌套深度仍基于整篇推导，以保证区间起始处的缩进正确。</para>
    /// </summary>
    public static string FormatRange(string source, int startLine, int endLine, int? tabSize = null, bool insertSpaces = true)
    {
        var unit = NormalizeUnit(tabSize);
        var lines = SplitLines(source, out var newline);
        if (startLine < 0) startLine = 0;
        if (endLine > lines.Length - 1) endLine = lines.Length - 1;

        var outLines = new string[lines.Length];
        var stack = new Stack<StackEntry>();
        for (var i = 0; i < lines.Length; i++)
        {
            // 区间外行也必须经 ProcessLine 更新缩进栈（否则区间起始处 depth 计算失准），
            // 但其输出保留原始内容（含原缩进/尾随空白），仅区间内的行用规整结果替换。
            var processed = ProcessLine(lines[i], stack, unit, insertSpaces);
            outLines[i] = (i >= startLine && i <= endLine) ? processed : lines[i];
        }
        return string.Join(newline, outLines);
    }

    private static int NormalizeUnit(int? tabSize) => tabSize is > 0 ? tabSize.Value : DefaultIndentUnit;

    private static string ProcessLine(string raw, Stack<StackEntry> stack, int unit, bool insertSpaces)
    {
        var trimmed = raw.Trim();
        // 注释 / 空行：保留前导缩进与内容，仅去除行尾空白（不重新缩进，避免改动作者意图）。
        if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("//"))
            return TrimTrailing(raw);

        var indent = CountIndent(raw);
        var firstWord = GetFirstWord(trimmed);

        // ── 花括号块关闭：} 弹栈 ──
        if (firstWord == "}")
        {
            if (stack.Count > 0) stack.Pop();
            // } 后可能跟续行关键字，如 "} else {" 或 "} else if {cond} {"
            var afterBrace = trimmed[1..].TrimStart();
            if (afterBrace.Length > 0)
            {
                var continuation = GetFirstWord(afterBrace);
                if (IsContinuation(continuation))
                {
                    var contDepth = stack.Count > 0 ? stack.Count - 1 : 0;
                    // 花括号续行块（"} else {"）：压栈使体 +1（由闭合 } 弹出）
                    if (afterBrace.EndsWith('{'))
                        stack.Push(new StackEntry(contDepth * unit, continuation, HasBody: false));
                    return Indent(contDepth * unit, insertSpaces) + "} " + afterBrace;
                }
                // } 后跟普通语句（如 "} say hi"），按当前栈深
                return Indent(stack.Count * unit, insertSpaces) + "} " + afterBrace;
            }
            return Indent(stack.Count * unit, insertSpaces) + trimmed;
        }

        int depth;
        if (IsContinuation(firstWord))
        {
            // else / elif：对齐到控制块同级，不弹不压。
            depth = stack.Count > 0 ? stack.Count - 1 : 0;
        }
        else if (DslBlockStructure.IsIndentationBlockEnder(firstWord))
        {
            // end：显式关闭块，弹栈。
            if (stack.Count > 0) stack.Pop();
            depth = stack.Count;
        }
        else if (DslBlockStructure.IsIndentationBlockStarter(firstWord))
        {
            // scene 在已有块内是导航命令（非块起始），不压栈。
            if (firstWord == "scene" && stack.Count > 0)
            {
                depth = stack.Count;
            }
            else
            {
                // 开块关键字：按自身缩进弹出同级或外层开块，然后压入自身。
                PopBlocksForStarter(stack, indent, firstWord);
                depth = stack.Count;
                stack.Push(new StackEntry(indent, firstWord, HasBody: false));
            }
        }
        else
        {
            // 普通体行——块结束判定与引擎 CompileBody 语义对齐：
            // ① 缩进浅于块 → 视为体行缩进错误，修复进体（不可能有意比块本身还浅）；
            // ② 缩进恰好等于块缩进且块已有真实体行 → 块结束，弹栈
            //    （while/if/menu 等缩进块无显式结束标记，块结束只能靠缩进回落识别；
            //     不弹栈会把块后语句永久吞进块体——如 while 后的语句全进循环体）；
            // ③ 缩进等于块缩进但块尚无体行 → 全平脚本的缩进错误，修复进体。
            // 真实体行（缩进深于块）标记 HasBody，供 ② 判定。
            while (stack.Count > 0 && stack.Peek().Indent == indent && stack.Peek().HasBody)
                stack.Pop();
            if (stack.Count > 0 && indent > stack.Peek().Indent)
                MarkHasBody(stack);
            depth = stack.Count;
        }

        return Indent(depth * unit, insertSpaces) + trimmed;
    }

    /// <summary>标记栈顶块已出现真实体行（record struct 值语义，需弹出重压）。</summary>
    private static void MarkHasBody(Stack<StackEntry> stack)
    {
        var top = stack.Pop();
        stack.Push(top with { HasBody = true });
    }

    /// <summary>
    /// 开块关键字入栈前的弹栈逻辑。
    /// <para>普通开块（if/scene/label/while 等）：缩进 &gt;= 当前行的上层开块全部关闭；</para>
    /// <para>case/default：只关闭同层或更深的前一个 case/default，不关闭 switch。</para>
    /// </summary>
    private static void PopBlocksForStarter(Stack<StackEntry> stack, int indent, string keyword)
    {
        if (keyword is "case" or "default")
        {
            while (stack.Count > 0 && stack.Peek().Keyword is "case" or "default" && stack.Peek().Indent >= indent)
                stack.Pop();
        }
        else
        {
            while (stack.Count > 0 && stack.Peek().Indent >= indent)
                stack.Pop();
        }
    }

    /// <summary>续行关键字：仅 <c>else</c>/<c>else if</c>（首词为 "else"）对齐到所属 <c>if</c> 同级、不开启新块层级。
    /// <c>case</c>/<c>default</c> 不在此——它们属开块（压栈），见 <see cref="DslBlockStructure.IsIndentationBlockStarter"/>。</summary>
    private static bool IsContinuation(string firstWord) =>
        DslBlockStructure.IsIndentationContinuation(firstWord);

    private static string Indent(int width, bool spaces) =>
        spaces ? new string(' ', width) : new string('\t', (width + 3) / 4);

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

    private static string GetFirstWord(string trimmed)
    {
        var idx = trimmed.IndexOf(' ');
        return idx < 0 ? trimmed : trimmed[..idx];
    }

    private static string TrimTrailing(string line)
    {
        var end = line.Length;
        while (end > 0 && (line[end - 1] == ' ' || line[end - 1] == '\t')) end--;
        return line[..end];
    }

    private static string[] SplitLines(string source, out string newline)
    {
        if (source.Contains("\r\n")) newline = "\r\n";
        else if (source.Contains('\r')) newline = "\r";
        else newline = "\n";
        return source.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
    }
}
