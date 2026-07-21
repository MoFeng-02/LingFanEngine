using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// SetVariableHandler / ExtendDialogHandler 集成测试（纯状态）。
/// </summary>
public class DialogHandlersTests2
{
    [Fact]
    public void SetVariableHandler_SetsPlainValue()
    {
        var ctx = new FakeCommandContext();
        new SetVariableHandler().Handle(new SetVariableCommand { Key = "hp", Value = 100 }, ctx);
        ctx.State.Get<object>("hp").Should().Be(100);
    }

    [Fact]
    public void SetVariableHandler_DefineOnce_DoesNotOverwriteExisting()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set("gold", 50);
        new SetVariableHandler().Handle(
            new SetVariableCommand { Key = "gold", Value = 999, IsDefine = true }, ctx);
        ctx.State.Get<object>("gold").Should().Be(50);
    }

    [Fact]
    public void SetVariableHandler_DefineOnce_SetsWhenMissing()
    {
        var ctx = new FakeCommandContext();
        new SetVariableHandler().Handle(
            new SetVariableCommand { Key = "level", Value = 3, IsDefine = true }, ctx);
        ctx.State.Get<object>("level").Should().Be(3);
    }

    [Fact]
    public void SetVariableHandler_EvaluatesExpressionPlaceholder()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set("gold", 10);
        new SetVariableHandler().Handle(
            new SetVariableCommand { Key = "result", Value = new DslExpressionPlaceholder("gold + 5") }, ctx);
        ctx.State.Get<object>("result").Should().Be(15);
    }

    [Fact]
    public void ExtendDialogHandler_AppendsToExistingText()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Dialog.Text, "你好");
        new ExtendDialogHandler().Handle(new ExtendDialogCommand { Append = "，世界" }, ctx);
        ctx.State.Get<string>(StateKeys.Dialog.Text).Should().Be("你好，世界");
        ctx.State.Get<bool>(StateKeys.Dialog.Complete).Should().BeFalse();
    }
}
