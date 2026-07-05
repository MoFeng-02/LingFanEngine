using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
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
}