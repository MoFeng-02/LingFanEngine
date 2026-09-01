using System.Globalization;
using Parlot;
using Parlot.Fluent;
using static Parlot.Fluent.Parsers;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 基于 Parlot 的表达式解析器——将表达式文本解析为 AST
/// <para>支持：算术/比较/逻辑运算符、函数调用、三元条件、变量路径、字符串/数字/布尔字面量</para>
/// <para>2026-09：由 Pidgin（含 Pidgin.Expression 优先级表）迁移至 Parlot 1.5.8 分层文法。</para>
/// <para>运算符优先级（从低到高）：</para>
/// <para>  ||      （逻辑或）</para>
/// <para>  &amp;&amp;      （逻辑与）</para>
/// <para>  == !=   （相等比较，非链式）</para>
/// <para>  &gt; &lt; &gt;= &lt;= （大小比较，非链式）</para>
/// <para>  + -     （加减）</para>
/// <para>  * / %   （乘除模）</para>
/// <para>  ! -     （一元取反/负号，链式）</para>
/// <para>  ()      （括号）</para>
/// <para>  .       （成员访问）</para>
/// <para>迁移语义对齐：① 比较运算符非链式（a==b==c 拒绝）——用单次 Optional 实现，
/// 与 Pidgin InfixN 行为一致（残留运算符无层级消费 → 顶层 Eof 失败）；
/// ② 一元运算符链式（--x/!!x）——自引用递归实现，对齐 PrefixChainable；
/// ③ 三元右结合——cond ? then : else 的 then/else 均递归到完整表达式。</para>
/// </summary>
public static class DslExpressionParser
{
    /// <summary>跳过空白（贪婪，零或多——与 Pidgin SkipWhitespaces 语义严格对齐）</summary>
    private static readonly Parser<TextSpan> _ws =
        ZeroOrOne(Literals.WhiteSpace(includeNewLines: true));

    /// <summary>匹配后跳过尾部空白——用于包裹 token 和运算符</summary>
    private static Parser<T> Tok<T>(Parser<T> p) => p.AndSkip(_ws);

    // ====== 基础 token ======

    /// <summary>标识符：[a-zA-Z_][a-zA-Z0-9_]*</summary>
    private static readonly Parser<string> Identifier =
        Capture(
            Literals.Pattern(c => char.IsLetter(c) || c == '_', 1, 1)
                .AndSkip(ZeroOrMany(Literals.Pattern(c => char.IsLetterOrDigit(c) || c == '_')))
        ).Then(s => s.ToString());

    /// <summary>无符号数字：123、123.45</summary>
    private static readonly Parser<Expr> Number =
        Capture(
            Literals.Pattern(char.IsDigit)
                .AndSkip(ZeroOrOne(
                    Literals.Char('.').AndSkip(Literals.Pattern(char.IsDigit))
                ))
        ).Then(s => MakeNumber(s.ToString()));

    /// <summary>转义字符解析：\" \\ \n \t \r</summary>
    /// <para>未知转义保留两字符原样（如 "C:\dir" 的 \d）——与 DslCore 的 DslParser/DslStatementParser
    /// 转义语义严格一致（原先此处会丢弃反斜杠导致有损）。</para>
    private static readonly Parser<string> _stringChar =
        Literals.Char('\\')
            .SkipAnd(Literals.Pattern(_ => true, 1, 1))
            .Then(c => c.ToString() switch
            {
                "\"" => "\"",
                "\\" => "\\",
                "n" => "\n",
                "t" => "\t",
                "r" => "\r",
                var other => "\\" + other,
            })
            .Or(Literals.NoneOf("\"\\").Then(c => c.ToString()));

    /// <summary>字符串字面量："..."，支持转义 \" \\ \n \t \r</summary>
    private static readonly Parser<Expr> StringLiteral =
        Literals.Char('"')
            .SkipAnd(ZeroOrMany(_stringChar).Then(ss => string.Concat(ss)))
            .AndSkip(Literals.Char('"'))
            .Then(s => (Expr)new LiteralExpr(s));

    /// <summary>变量引用或关键字（true/false/null）：identifier ('.' identifier)*</summary>
    private static readonly Parser<Expr> VariableOrKeyword =
        Separated(Literals.Char('.'), Identifier)
            .Then(parts =>
            {
                var path = string.Join(".", parts);
                return path switch
                {
                    "true" => (Expr)new LiteralExpr(true),
                    "false" => (Expr)new LiteralExpr(false),
                    "null" => (Expr)new LiteralExpr(null),
                    _ => new VariableExpr(path)
                };
            });

    // ====== 最终解析器 ======

    /// <summary>
    /// 分层文法构建（Parlot 无 Pidgin.Expression 优先级表等价物，按层级文法自底向上组装）：
    /// <para>ternary  := orExpr ("?" ternary ":" ternary)?</para>
    /// <para>orExpr   := andExpr ("||" andExpr)*</para>
    /// <para>andExpr  := eqExpr ("&&" eqExpr)*</para>
    /// <para>eqExpr   := relExpr (("=="|"!=") relExpr)?        非链式</para>
    /// <para>relExpr  := addExpr (("&gt;="|"&lt;="|"&gt;"|"&lt;") addExpr)?   非链式</para>
    /// <para>addExpr  := mulExpr (("+"|"-") mulExpr)*</para>
    /// <para>mulExpr  := unary (("*"|"/"|"%") unary)*</para>
    /// <para>unary    := ("!"|"-") unary | primary             链式</para>
    /// <para>primary  := number | string | funcCall | variable | "(" fullExpr ")"</para>
    /// </summary>
    private static readonly Parser<Expr> _parser = BuildParser();

    private static Parser<Expr> BuildParser()
    {
        // 前向引用——完整表达式（括号与函数参数的递归入口）
        var fullExpr = Deferred<Expr>();

        // 函数调用：identifier ( expr, expr, ... )
        var functionCall =
            Tok(Identifier)
                .And(Between(
                    Tok(Literals.Char('(')),
                    Separated(Tok(Literals.Char(',')), fullExpr),
                    Tok(Literals.Char(')'))))
                .Then(t => (Expr)new FunctionCallExpr(t.Item1, t.Item2.ToList()));

        // Term 解析器——原子表达式（数字/字符串/函数调用/变量/括号表达式）
        // 分支顺序与 Pidgin 原实现一致：函数调用在变量之前（无括号时自动回溯到变量）
        var primary =
            Number
                .Or(StringLiteral)
                .Or(functionCall)
                .Or(VariableOrKeyword)
                .Or(Tok(Literals.Char('(')).SkipAnd(fullExpr).AndSkip(Tok(Literals.Char(')'))))
                .AndSkip(_ws);

        // 一元运算符：! -（链式，对齐 Pidgin PrefixChainable）
        var unary = Recursive<Expr>(self =>
            Tok(PrefixOp())
                .And(self)
                .Then(t => (Expr)new UnaryExpr(t.Item1, t.Item2))
                .Or(primary));

        // 二元运算符（长运算符在前，避免 ">=" 被 ">" 截断）
        Parser<string> BinaryOp(params string[] ops) =>
            ops.Select(o => Tok(Literals.Text(o))).Aggregate((a, b) => a.Or(b));
        Parser<string> PrefixOp() => BinaryOp("!", "-");

        // 链式二元层（左结合）：operand (op operand)*
        Parser<Expr> Chain(Parser<Expr> operand, params string[] ops) =>
            operand
                .And(ZeroOrMany(BinaryOp(ops).And(operand)))
                .Then(t => FoldLeft(t.Item1, t.Item2));

        // 非链式二元层（对齐 Pidgin InfixN）：operand (op operand)?
        // 残留同级运算符无层级消费 → 顶层 Eof 失败 → a==b==c 拒绝
        Parser<Expr> NonChain(Parser<Expr> operand, params string[] ops) =>
            operand
                .And(BinaryOp(ops).And(operand).Optional())
                .Then(t => t.Item2.HasValue
                    ? (Expr)new BinaryExpr(t.Item1, t.Item2.Value.Item1, t.Item2.Value.Item2)
                    : t.Item1);

        var multiplicative = Chain(unary, "*", "/", "%");
        var additive = Chain(multiplicative, "+", "-");
        // 大小比较：>= <= 必须在 > < 之前（运算符前缀冲突）
        var relational = NonChain(additive, ">=", "<=", ">", "<");
        var equality = NonChain(relational, "==", "!=");
        var logicalAnd = Chain(equality, "&&");
        var logicalOr = Chain(logicalAnd, "||");

        // 三元条件（右结合）：cond ? then : else——then/else 均递归完整表达式；
        // "?" 不匹配时自动回溯到 logicalOr（Parlot 失败自动复位游标，无需 Try）
        var ternary = Recursive<Expr>(self =>
            logicalOr
                .And(Tok(Literals.Text("?")).SkipAnd(self)
                    .AndSkip(Tok(Literals.Text(":")))
                    .And(self))
                .Then(t => (Expr)new ConditionalExpr(t.Item1, t.Item2.Item1, t.Item2.Item2))
                .Or(logicalOr));

        fullExpr.Parser = ternary;

        // 跳过前导空白，解析完整表达式，确保到达输入末尾
        return _ws.SkipAnd(fullExpr).Eof();
    }

    // ====== 公共 API ======

    /// <summary>
    /// 解析表达式文本为 AST
    /// </summary>
    /// <param name="input">表达式文本（不含花括号）</param>
    /// <returns>解析成功返回 AST；失败返回 null（原 Pidgin Result&lt;char,Expr&gt; 收窄为 nullable）</returns>
    public static Expr? Parse(string input) =>
        _parser.TryParse(input, out var expr) ? expr : null;

    /// <summary>
    /// 尝试解析表达式文本
    /// </summary>
    public static bool TryParse(string input, out Expr? expr, out string? error)
    {
        if (_parser.TryParse(input, out var value, out var parseError))
        {
            expr = value;
            error = null;
            return true;
        }
        expr = null;
        error = parseError?.Message ?? "Unknown parse error";
        return false;
    }

    // ====== 辅助方法 ======

    /// <summary>左结合折叠：((a op b) op c) ...</summary>
    private static Expr FoldLeft(Expr first, IReadOnlyList<(string op, Expr operand)> rest)
    {
        var result = first;
        foreach (var (op, operand) in rest)
            result = new BinaryExpr(result, op, operand);
        return result;
    }

    /// <summary>
    /// 将数字字符串转换为 LiteralExpr——整数返回 int，浮点数返回 double
    /// </summary>
    private static Expr MakeNumber(string s)
    {
        var d = double.Parse(s, CultureInfo.InvariantCulture);
        return new LiteralExpr(d == (int)d ? (object)(int)d : d);
    }
}
