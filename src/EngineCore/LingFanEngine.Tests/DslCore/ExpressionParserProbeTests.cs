using FluentAssertions;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.DslCore;

/// <summary>
/// 表达式解析器探针（2026-09 Parlot 迁移审计）：
/// DslExpressionParser 此前零测试覆盖——if/while/for/switch 的 {条件} 全靠它。
/// 本组钉死运算符优先级、非链式比较、一元链、三元、字符串/函数/成员访问等核心行为。
/// </summary>
public class ExpressionParserProbeTests
{
    private static LingFanEngine.Services.Core.Expr? Parse(string s) => DslExpressionParser.Parse(s);

    private static string Kind(Expr? e) => e?.GetType().Name ?? "null";

    // ====== 字面量与变量 ======

    [Theory]
    [InlineData("42", "LiteralExpr")]
    [InlineData("3.14", "LiteralExpr")]
    [InlineData("\"hello\"", "LiteralExpr")]
    [InlineData("true", "LiteralExpr")]
    [InlineData("false", "LiteralExpr")]
    [InlineData("null", "LiteralExpr")]
    [InlineData("gold", "VariableExpr")]
    [InlineData("player.items", "VariableExpr")]
    public void LiteralsAndVariables(string input, string kind)
    {
        Kind(Parse(input)).Should().Be(kind, $"[{input}] 应解析为 {kind}");
    }

    // ====== 运算符优先级（宪法红线：* / % 高于 + -，高于比较，高于 &&，高于 ||）======

    [Fact]
    public void Precedence_MulBeforeAdd()
    {
        // 1 + 2 * 3 → 1 + (2*3)，不能是 (1+2)*3
        var e = Parse("1 + 2 * 3");
        var add = e.Should().BeOfType<BinaryExpr>().Subject;
        add.Op.Should().Be("+");
        add.Right.Should().BeOfType<BinaryExpr>().Subject.Op.Should().Be("*");
    }

    [Fact]
    public void Precedence_AddBeforeCompare()
    {
        // a + 1 > b → (a+1) > b
        var e = Parse("a + 1 > b");
        var cmp = e.Should().BeOfType<BinaryExpr>().Subject;
        cmp.Op.Should().Be(">");
        cmp.Left.Should().BeOfType<BinaryExpr>().Subject.Op.Should().Be("+");
    }

    [Fact]
    public void Precedence_CompareBeforeAnd()
    {
        // a > 1 && b < 2 → (a>1) && (b<2)
        var e = Parse("a > 1 && b < 2");
        var and = e.Should().BeOfType<BinaryExpr>().Subject;
        and.Op.Should().Be("&&");
        and.Left.Should().BeOfType<BinaryExpr>().Subject.Op.Should().Be(">");
        and.Right.Should().BeOfType<BinaryExpr>().Subject.Op.Should().Be("<");
    }

    [Fact]
    public void Precedence_AndBeforeOr()
    {
        // a || b && c → a || (b && c)
        var e = Parse("a || b && c");
        var or = e.Should().BeOfType<BinaryExpr>().Subject;
        or.Op.Should().Be("||");
        or.Right.Should().BeOfType<BinaryExpr>().Subject.Op.Should().Be("&&");
    }

    // ====== 非链式红线：a == b == c 必须被拒绝（Pidgin InfixN 行为）======

    [Theory]
    [InlineData("a == b == c")]
    [InlineData("a != b != c")]
    [InlineData("1 < 2 < 3")]
    [InlineData("a >= b > c")]
    public void NonChainableComparisons_Rejected(string input)
    {
        Parse(input).Should().BeNull($"[{input}] 比较运算符非链式，链式应被拒绝");
    }

    // ====== 链式运算（左结合折叠）======

    [Fact]
    public void Additive_IsLeftAssociative()
    {
        // a - b - c → (a-b)-c
        var e = Parse("a - b - c");
        var sub = e.Should().BeOfType<BinaryExpr>().Subject;
        sub.Op.Should().Be("-");
        sub.Left.Should().BeOfType<BinaryExpr>().Subject.Op.Should().Be("-");
    }

    // ====== 一元链式（--x / !!x）与负数字面量 ======

    [Theory]
    [InlineData("--x")]
    [InlineData("!!done")]
    [InlineData("-!x")]
    public void UnaryOperators_Chain(string input)
    {
        Parse(input).Should().NotBeNull($"[{input}] 一元运算符应可链式");
    }

    [Fact]
    public void NegativeNumber_InComparison()
    {
        // i > -1（循环常见形态：倒计时）
        var e = Parse("i > -1");
        var cmp = e.Should().BeOfType<BinaryExpr>().Subject;
        cmp.Op.Should().Be(">");
        cmp.Right.Should().BeOfType<UnaryExpr>().Subject.Op.Should().Be("-");
    }

    // ====== 三元（右结合）======

    [Fact]
    public void Ternary_Parses()
    {
        var e = Parse("a ? b : c");
        e.Should().BeOfType<ConditionalExpr>();
    }

    [Fact]
    public void Ternary_RightAssociative_Nested()
    {
        // a ? b : c ? d : e → a ? b : (c ? d : e)
        var e = Parse("a ? b : c ? d : e");
        var cond = e.Should().BeOfType<ConditionalExpr>().Subject;
        cond.ElseExpr.Should().BeOfType<ConditionalExpr>(
            "三元右结合：else 分支应递归解析为嵌套三元");
    }

    [Fact]
    public void Ternary_NestedInThen()
    {
        var e = Parse("a ? b ? c : d : e");
        var cond = e.Should().BeOfType<ConditionalExpr>().Subject;
        cond.ThenExpr.Should().BeOfType<ConditionalExpr>();
    }

    // ====== 括号与函数调用 ======

    [Fact]
    public void Parens_OverridePrecedence()
    {
        // (1 + 2) * 3
        var e = Parse("(1 + 2) * 3");
        var mul = e.Should().BeOfType<BinaryExpr>().Subject;
        mul.Op.Should().Be("*");
        mul.Left.Should().BeOfType<BinaryExpr>().Subject.Op.Should().Be("+");
    }

    [Fact]
    public void FunctionCall_WithArgs()
    {
        var e = Parse("max(a, 1 + 2)");
        var call = e.Should().BeOfType<FunctionCallExpr>().Subject;
        call.FunctionName.Should().Be("max");
        call.Arguments.Should().HaveCount(2);
        call.Arguments[1].Should().BeOfType<BinaryExpr>().Subject.Op.Should().Be("+");
    }

    // ====== 复合循环条件（while/for 实战形态）======

    [Theory]
    [InlineData("i < 5")]
    [InlineData("i <= count - 1")]
    [InlineData("i < len && !done")]
    [InlineData("i >= 0 || retry")]
    [InlineData("flag == true")]
    [InlineData("i % 2 == 0")]
    [InlineData("count > 0 ? a : b")]
    public void LoopConditionForms_AllParse(string input)
    {
        Parse(input).Should().NotBeNull($"循环条件形态 [{input}] 必须可解析");
    }

    // ====== 拒绝形态 ======

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a +")]
    [InlineData("(a")]
    [InlineData("a ? b")]
    public void MalformedExpressions_Rejected(string input)
    {
        Parse(input).Should().BeNull($"[{input}] 残缺表达式应被拒绝");
    }
}
