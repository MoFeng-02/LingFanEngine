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

    // ====== let/local 局部变量可读（B47 修复回归）======
    // let/local 写入 _local_<name>，say 插值必须能从 _local_ 前缀读到，否则局部变量「只能写不能读」。

    [Fact]
    public void ShowDialogHandler_InterpolatesLocalVariable_Let()
    {
        // 模拟 DSL: let "temp" 42  -> SetVariableCommand{ Key="_local_temp", Value=42, IsDefine=true }
        var ctx = new FakeCommandContext();
        new SetVariableHandler().Handle(new SetVariableCommand { Key = "_local_temp", Value = 42, IsDefine = true }, ctx);
        new ShowDialogHandler().Handle(new ShowDialogCommand { Text = "temp={temp}" }, ctx);
        ctx.State.Get<string>(StateKeys.Dialog.Text).Should().Be("temp=42");
    }

    [Fact]
    public void ShowDialogHandler_LocalShadowsGlobal()
    {
        // set "x" 10（全局 x） + let "x" 99（局部 _local_x） -> 读取应取局部
        var ctx = new FakeCommandContext();
        new SetVariableHandler().Handle(new SetVariableCommand { Key = "x", Value = 10 }, ctx);
        new SetVariableHandler().Handle(new SetVariableCommand { Key = "_local_x", Value = 99, IsDefine = true }, ctx);
        new ShowDialogHandler().Handle(new ShowDialogCommand { Text = "x={x}" }, ctx);
        ctx.State.Get<string>(StateKeys.Dialog.Text).Should().Be("x=99");
    }

    [Fact]
    public void ShowDialogHandler_InterpolatesLocalDottedKey()
    {
        // 模拟 DSL: let "player.hp" 50  -> 键为 _local_player_hp
        var ctx = new FakeCommandContext();
        new SetVariableHandler().Handle(new SetVariableCommand { Key = "_local_player_hp", Value = 50, IsDefine = true }, ctx);
        new ShowDialogHandler().Handle(new ShowDialogCommand { Text = "HP={player.hp}" }, ctx);
        ctx.State.Get<string>(StateKeys.Dialog.Text).Should().Be("HP=50");
    }
}
