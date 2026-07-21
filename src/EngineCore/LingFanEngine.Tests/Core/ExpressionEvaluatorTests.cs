using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.DslCore;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using Xunit;

namespace LingFanEngine.Tests.Core;

public class ExpressionEvaluatorTests
{
    private readonly IStateContainer _state;

    public ExpressionEvaluatorTests()
    {
        _state = new StateContainer();
    }

    [Fact]
    public void Evaluate_PureVariable()
    {
        _state.Set("gold", 100);
        var result = DslExpressionEvaluator.Evaluate("gold", _state);
        result.Should().Be(100);
    }

    [Fact]
    public void Evaluate_Arithmetic()
    {
        _state.Set("gold", 100);
        var result = DslExpressionEvaluator.Evaluate("gold + 50", _state);
        result.Should().Be(150);
    }

    [Fact]
    public void Evaluate_Compare()
    {
        _state.Set("gold", 100);
        var result = DslExpressionEvaluator.Evaluate("gold >= 100", _state);
        result.Should().Be(true);
    }

    [Fact]
    public void Evaluate_Random()
    {
        var result = DslExpressionEvaluator.Evaluate("random(1, 6)", _state);
        result.Should().BeOfType<int>();
        var value = (int)result!;
        value.Should().BeInRange(1, 6);
    }

    [Fact]
    public void Evaluate_Bool_True()
    {
        var result = DslExpressionEvaluator.Evaluate("true", _state);
        result.Should().Be(true);
    }

    [Fact]
    public void Evaluate_Bool_False()
    {
        var result = DslExpressionEvaluator.Evaluate("false", _state);
        result.Should().Be(false);
    }

    [Fact]
    public void Evaluate_String()
    {
        var result = DslExpressionEvaluator.Evaluate("\"hello\"", _state);
        result.Should().Be("hello");
    }

    [Fact]
    public void EvaluateBool_True()
    {
        _state.Set("gold", 100);
        var result = DslExpressionEvaluator.EvaluateBool("gold >= 100", _state);
        result.Should().BeTrue();
    }

    [Fact]
    public void EvaluateBool_False()
    {
        _state.Set("gold", 50);
        var result = DslExpressionEvaluator.EvaluateBool("gold >= 100", _state);
        result.Should().BeFalse();
    }

    [Fact]
    public void ReplaceText_Variable()
    {
        _state.Set("gold", 100);
        var result = DslExpressionEvaluator.ReplaceText("你有 {gold} 金币", _state);
        result.Should().Be("你有 100 金币");
    }

    [Fact]
    public void ReplaceText_MultipleVariables()
    {
        _state.Set("name", "小明");
        _state.Set("gold", 100);
        var result = DslExpressionEvaluator.ReplaceText("{name} 有 {gold} 金币", _state);
        result.Should().Be("小明 有 100 金币");
    }

    [Fact]
    public void ReplaceText_Format()
    {
        _state.Set("mins", 5);
        var result = DslExpressionEvaluator.ReplaceText("{mins:00}", _state);
        result.Should().Be("05");
    }

    [Fact]
    public void RegisterFunction_Custom()
    {
        // 注册自定义函数
        DslExpressionEvaluator.RegisterFunction("add10", (args, _) => (int)args[0]! + 10);

        var result = DslExpressionEvaluator.Evaluate("add10(5)", _state);
        result.Should().Be(15);

        // 清理
        DslExpressionEvaluator.UnregisterFunction("add10");
    }

    // ====== Phase 39: define + dotted key 测试 ======

    [Fact]
    public void ReplaceText_DottedKey_FlatStorage()
    {
        // Simulate MergeIntoState: Set("player.name", "玩家") stores as flat key
        _state.Set("player.name", "玩家");
        _state.Set("player.gold", 100);
        _state.Set("player.hp", 50);
        _state.Set("player.maxHp", 100);

        var result = DslExpressionEvaluator.ReplaceText(
            "{player.name} · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}", _state);
        result.Should().Be("玩家 · 金币: 100 · HP: 50/100");
    }

    [Fact]
    public void Evaluate_DottedKey_FlatStorage()
    {
        _state.Set("player.name", "玩家");
        var result = DslExpressionEvaluator.Evaluate("player.name", _state);
        result.Should().Be("玩家");
    }

    [Fact]
    public void ParseDefineLine_DottedKey()
    {
        var entry = DslParser.ParseDefineLine("define \"player.name\" \"玩家\" once");
        entry.Should().NotBeNull();
        entry!.Key.Should().Be("player.name");
        entry.RawValue.Should().Be("\"玩家\"");
    }

    [Fact]
    public void ParseDefineLine_DottedKey_Number()
    {
        var entry = DslParser.ParseDefineLine("define \"player.hp\" 50 once");
        entry.Should().NotBeNull();
        entry!.Key.Should().Be("player.hp");
        entry.RawValue.Should().Be("50");
    }

    [Fact]
    public void StateContainer_DottedKey_SetAndGet()
    {
        // Simulate what MergeIntoState does: state.Set("player.name", "玩家")
        _state.Set("player.name", "玩家");

        // Get<object> should find it as flat key
        var result = _state.Get<object>("player.name");
        result.Should().Be("玩家");

        // ContainsKey should return true
        _state.ContainsKey("player.name").Should().BeTrue();
    }

    [Fact]
    public void StateContainer_DottedKey_MultipleKeys()
    {
        _state.Set("player.name", "玩家");
        _state.Set("player.hp", 50);
        _state.Set("player.maxHp", 100);
        _state.Set("player.gold", 100);

        _state.Get<object>("player.name").Should().Be("玩家");
        _state.Get<object>("player.hp").Should().Be(50);
        _state.Get<object>("player.maxHp").Should().Be(100);
        _state.Get<object>("player.gold").Should().Be(100);
    }

    /// <summary>
    /// 这是 ControlFactory.ConvertToControl 实际使用的替换方法
    /// </summary>
    [Fact]
    public void ExpressionParser_Replace_DottedKey_FlatStorage()
    {
        _state.Set("player.name", "玩家");
        _state.Set("player.gold", 100);
        _state.Set("player.hp", 50);
        _state.Set("player.maxHp", 100);

        var result = ExpressionParser.Replace(
            "{player.name} · 金币: {player.gold} · HP: {player.hp}/{player.maxHp}", _state);
        result.Should().Be("玩家 · 金币: 100 · HP: 50/100");
    }

    /// <summary>
    /// ExpressionParser.Replace 在值为 null 时返回原始表达式文本（与 DslExpressionEvaluator.ReplaceText 不同）
    /// </summary>
    [Fact]
    public void ExpressionParser_Replace_NullValue_ReturnsRawExpr()
    {
        // 不设置 player.name，模拟值未注入的情况
        var result = ExpressionParser.Replace("{player.name}", _state);
        // ExpressionParser.Replace 在值为 null 时返回原始表达式
        result.Should().Be("player.name");
    }

    // ====== S3 (B7): ==/!= 类型归一化（真实缺陷修复回归） ======

    [Fact]
    public void Evaluate_Equal_IntVsDoubleLiteral()
    {
        // B7 核心：int 0 与 double 0.0 因装箱类型不同，旧实现 object.Equals 误判不等
        DslExpressionEvaluator.Evaluate("0 == 0.0", _state).Should().Be(true);
    }

    [Fact]
    public void Evaluate_NotEqual_IntVsDoubleLiteral()
    {
        DslExpressionEvaluator.Evaluate("0 != 0.0", _state).Should().Be(false);
    }

    [Fact]
    public void Evaluate_Equal_IntStateVsDoubleLiteral()
    {
        // 真实场景：整型状态 vs 浮点字面量
        _state.Set("gold", 100);
        DslExpressionEvaluator.Evaluate("gold == 100.0", _state).Should().Be(true);
    }

    [Fact]
    public void Evaluate_NotEqual_IntStateVsDoubleLiteral()
    {
        _state.Set("gold", 100);
        DslExpressionEvaluator.Evaluate("gold != 100.0", _state).Should().Be(false);
    }

    [Fact]
    public void Evaluate_Equal_DifferentNumericValue()
    {
        _state.Set("gold", 100);
        DslExpressionEvaluator.Evaluate("gold == 101.0", _state).Should().Be(false);
    }

    [Fact]
    public void Evaluate_Equal_String()
    {
        DslExpressionEvaluator.Evaluate("\"a\" == \"a\"", _state).Should().Be(true);
    }

    [Fact]
    public void Evaluate_Equal_String_Different()
    {
        DslExpressionEvaluator.Evaluate("\"a\" == \"b\"", _state).Should().Be(false);
    }

    [Fact]
    public void Evaluate_Equal_NullOperandVsZero()
    {
        // 未设置变量视为 null，与数值比较判不等（锁定既有语义，非行为变更）
        DslExpressionEvaluator.Evaluate("missing == 0", _state).Should().Be(false);
    }

    [Fact]
    public void Evaluate_Equal_Bool()
    {
        DslExpressionEvaluator.Evaluate("true == true", _state).Should().Be(true);
        DslExpressionEvaluator.Evaluate("true == false", _state).Should().Be(false);
    }

    [Fact]
    public void EvaluateBool_Equal_IntStateVsDoubleLiteral()
    {
        _state.Set("gold", 100);
        DslExpressionEvaluator.EvaluateBool("gold == 100.0", _state).Should().BeTrue();
        DslExpressionEvaluator.EvaluateBool("gold != 100.0", _state).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_Equal_LongVsInt()
    {
        _state.Set("big", 300L);
        DslExpressionEvaluator.Evaluate("big == 300", _state).Should().Be(true);
    }

    [Fact]
    public void Evaluate_Number_DecimalLiteral_NotMangled()
    {
        // 解析器 bug 回归：小数点曾被丢弃，100.0 误拼为 1000、0.5 误拼为 05
        DslExpressionEvaluator.Evaluate("0.5", _state).Should().Be(0.5);
        DslExpressionEvaluator.Evaluate("100.0", _state).Should().Be(100.0);
    }

    [Fact]
    public void Evaluate_Arithmetic_Decimal()
    {
        DslExpressionEvaluator.Evaluate("1.5 + 1.2", _state).Should().Be(2.7);
    }

    // ====== E3 延伸：三元 / 一元 / 短路（行为锁定，非 bug） ======

    [Fact]
    public void Evaluate_Ternary_TrueBranch()
    {
        _state.Set("gold", 100);
        DslExpressionEvaluator.Evaluate("gold >= 100 ? 1 : 2", _state).Should().Be(1);
    }

    [Fact]
    public void Evaluate_Ternary_FalseBranch()
    {
        _state.Set("gold", 50);
        DslExpressionEvaluator.Evaluate("gold >= 100 ? 1 : 2", _state).Should().Be(2);
    }

    [Fact]
    public void Evaluate_Unary_Minus()
    {
        _state.Set("gold", 50);
        DslExpressionEvaluator.Evaluate("-gold", _state).Should().Be(-50.0);
    }

    [Fact]
    public void EvaluateBool_Unary_Not()
    {
        _state.Set("gold", 50);
        DslExpressionEvaluator.EvaluateBool("!(gold >= 100)", _state).Should().BeTrue();
    }

    [Fact]
    public void EvaluateBool_ShortCircuit_And()
    {
        _state.Set("gold", 50);
        DslExpressionEvaluator.EvaluateBool("gold >= 100 && gold < 200", _state).Should().BeFalse();
        DslExpressionEvaluator.EvaluateBool("gold >= 50 && gold < 200", _state).Should().BeTrue();
    }

    [Fact]
    public void EvaluateBool_ShortCircuit_Or()
    {
        _state.Set("gold", 100);
        DslExpressionEvaluator.EvaluateBool("gold >= 100 || gold < 0", _state).Should().BeTrue();
    }
}