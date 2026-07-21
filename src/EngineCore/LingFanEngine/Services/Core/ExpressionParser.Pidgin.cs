using System.Globalization;
using Pidgin;
using Pidgin.Expression;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using PidginExprParser = Pidgin.Expression.ExpressionParser;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 基于 Pidgin 的表达式解析器——将表达式文本解析为 AST
/// <para>支持：算术/比较/逻辑运算符、函数调用、三元条件、变量路径、字符串/数字/布尔字面量</para>
/// <para>运算符优先级（从低到高）：</para>
/// <para>  ||      （逻辑或）</para>
/// <para>  &amp;&amp;      （逻辑与）</para>
/// <para>  == !=   （相等比较）</para>
/// <para>  &gt; &lt; &gt;= &lt;= （大小比较）</para>
/// <para>  + -     （加减）</para>
/// <para>  * / %   （乘除模）</para>
/// <para>  ! -     （一元取反/负号）</para>
/// <para>  ()      （括号）</para>
/// <para>  .       （成员访问）</para>
/// </summary>
public static class DslExpressionParser
{
    /// <summary>
    /// 跳过尾部空白——用于包裹 token 和运算符
    /// </summary>
    private static Parser<char, T> Tok<T>(Parser<char, T> p) => p.Before(SkipWhitespaces);

    // ====== 基础 token ======

    /// <summary>
    /// 标识符：[a-zA-Z_][a-zA-Z0-9_]*
    /// </summary>
    private static readonly Parser<char, string> Identifier =
        Token(c => char.IsLetter(c) || c == '_')
            .Then(first => Token(c => char.IsLetterOrDigit(c) || c == '_').ManyString()
                .Select(rest => first + rest))
            .Labelled("identifier");

    /// <summary>
    /// 无符号数字：123、123.45
    /// </summary>
    private static readonly Parser<char, Expr> Number =
        (from intPart in Digit.AtLeastOnceString()
         from fracPart in Try(Char('.').Then(Digit.AtLeastOnceString())).Optional()
         select MakeNumber(intPart + (fracPart.HasValue ? "." + fracPart.Value : "")))
        .Labelled("number");

    /// <summary>
    /// 转义字符解析：\" \\ \n \t \r
    /// <para>声明在 StringLiteral 之前，确保静态初始化顺序正确。</para>
    /// </summary>
    private static readonly Parser<char, string> _stringChar =
        Char('\\').Then(Any.Select(c => c switch
        {
            'n' => "\n",
            't' => "\t",
            'r' => "\r",
            _ => c.ToString()
        }))
        .Or(AnyCharExcept('"', '\\').Select(c => c.ToString()));

    /// <summary>
    /// 字符串字面量："..."，支持转义 \" \\ \n \t \r
    /// </summary>
    private static readonly Parser<char, Expr> StringLiteral =
        Char('"').Then(_stringChar.Many().Select(cs => string.Concat(cs)))
            .Before(Char('"'))
            .Select(s => (Expr)new LiteralExpr(s))
            .Labelled("string");


    /// <summary>
    /// 变量引用或关键字（true/false/null）：identifier ('.' identifier)*
    /// </summary>
    private static readonly Parser<char, Expr> VariableOrKeyword =
        Identifier.Separated(Char('.'))
            .Select(parts =>
            {
                var path = string.Join(".", parts);
                return path switch
                {
                    "true" => (Expr)new LiteralExpr(true),
                    "false" => (Expr)new LiteralExpr(false),
                    "null" => (Expr)new LiteralExpr(null),
                    _ => new VariableExpr(path)
                };
            })
            .Labelled("variable");

    /// <summary>
    /// 函数调用：identifier ( expr, expr, ... )
    /// </summary>
    private static Parser<char, Expr> FunctionCall(Parser<char, Expr> expr) =>
        from name in Tok(Identifier)
        from args in expr.Separated(Tok(Char(','))).Between(Tok(Char('(')), Tok(Char(')')))
        select (Expr)new FunctionCallExpr(name, args.ToList());

    /// <summary>
    /// Term 解析器——原子表达式（数字/字符串/函数调用/变量/括号表达式）
    /// </summary>
    private static Parser<char, Expr> Term(Parser<char, Expr> fullExpr) =>
        Tok(OneOf(
            Number,
            StringLiteral,
            Try(FunctionCall(fullExpr)),
            VariableOrKeyword,
            fullExpr.Between(Tok(Char('(')), Tok(Char(')')))
        ));

    // ====== 运算符表 ======

    /// <summary>
    /// 二元运算符辅助——匹配运算符字符串后跳过空白，返回 BinaryExpr 构造器
    /// </summary>
    private static Parser<char, Func<Expr, Expr, Expr>> BinaryOp(string opStr) =>
        Try(String(opStr)).Before(SkipWhitespaces)
            .ThenReturn<Func<Expr, Expr, Expr>>((l, r) => new BinaryExpr(l, opStr, r));

    /// <summary>
    /// 前缀运算符辅助
    /// </summary>
    private static Parser<char, Func<Expr, Expr>> PrefixOp(string opStr) =>
        Try(String(opStr)).Before(SkipWhitespaces)
            .ThenReturn<Func<Expr, Expr>>(x => new UnaryExpr(opStr, x));

    /// <summary>
    /// 运算符优先级表（从高到低）
    /// </summary>
    private static readonly OperatorTableRow<char, Expr>[] _operatorTable =
    [
        // 1. 前缀：! -
        Operator.PrefixChainable(PrefixOp("!"), PrefixOp("-")),
        // 2. 乘除模 * / %
        Operator.InfixL(BinaryOp("*"))
            .And(Operator.InfixL(BinaryOp("/")))
            .And(Operator.InfixL(BinaryOp("%"))),
        // 3. 加减 + -
        Operator.InfixL(BinaryOp("+"))
            .And(Operator.InfixL(BinaryOp("-"))),
        // 4. 大小比较 > < >= <=
        Operator.InfixN(BinaryOp(">="))
            .And(Operator.InfixN(BinaryOp("<=")))
            .And(Operator.InfixN(BinaryOp(">")))
            .And(Operator.InfixN(BinaryOp("<"))),
        // 5. 相等比较 == !=
        Operator.InfixN(BinaryOp("=="))
            .And(Operator.InfixN(BinaryOp("!="))),
        // 6. 逻辑与 &&
        Operator.InfixL(BinaryOp("&&")),
        // 7. 逻辑或 ||
        Operator.InfixL(BinaryOp("||")),
    ];

    /// <summary>
    /// 最终解析器——延迟构建，避免静态字段初始化顺序导致的循环依赖。
    /// <para>所有 parser 组合子在 BuildParser 内部构建，此时基础 token 解析器（Identifier、Number 等）已完成初始化。</para>
    /// </summary>
    private static readonly Lazy<Parser<char, Expr>> _parserLazy = new(BuildParser);

    /// <summary>
    /// 构建 最终解析器——在方法内部组装二元表达式与三元条件表达式，打破循环依赖
    /// </summary>
    private static Parser<char, Expr> BuildParser()
    {
        // 前向引用——延迟访问完整表达式解析器
        Parser<char, Expr>? fullExprRef = null;
        var fullExprForward = Rec<char, Expr>(() => fullExprRef!);

        // 二元表达式（通过 Pidgin ExpressionParser.Build 构建）
        // Term 内部使用 fullExprForward 处理括号和函数调用的递归
        var binaryExpr = PidginExprParser.Build<char, Expr>(
            _ => Term(fullExprForward),
            _operatorTable
        );

        // 完整表达式——在二元表达式之上叠加三元条件运算 cond ? a : b
        // 注意：整个三元分支用 Try 包裹，确保 ? 不匹配时回退 binaryExpr 的输入消费
        fullExprRef = Rec<char, Expr>(self =>
            Try(from cond in binaryExpr
                from question in Tok(String("?"))
                from thenExpr in self
                from colon in Tok(String(":"))
                from elseExpr in self
                select (Expr)new ConditionalExpr(cond, thenExpr, elseExpr))
            .Or(binaryExpr)
        );

        // 跳过前导空白，解析完整表达式，确保到达输入末尾
        return SkipWhitespaces.Then(fullExprRef.Before(End));
    }

    // ====== 公共 API ======

    /// <summary>
    /// 解析表达式文本为 AST
    /// </summary>
    /// <param name="input">表达式文本（不含花括号）</param>
    /// <returns>解析结果</returns>
    public static Result<char, Expr> Parse(string input) => _parserLazy.Value.Parse(input);

    /// <summary>
    /// 尝试解析表达式文本
    /// </summary>
    /// <param name="input">表达式文本</param>
    /// <param name="expr">解析成功的 AST</param>
    /// <param name="error">解析失败时的错误信息</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParse(string input, out Expr? expr, out string? error)
    {
        var result = Parse(input);
        if (result.Success)
        {
            expr = result.Value;
            error = null;
            return true;
        }
        expr = null;
        error = result.Error?.ToString() ?? "Unknown parse error";
        return false;
    }

    // ====== 辅助方法 ======

    /// <summary>
    /// 将数字字符串转换为 LiteralExpr——整数返回 int，浮点数返回 double
    /// </summary>
    private static Expr MakeNumber(string s)
    {
        var d = double.Parse(s, CultureInfo.InvariantCulture);
        return new LiteralExpr(d == (int)d ? (object)(int)d : d);
    }
}
