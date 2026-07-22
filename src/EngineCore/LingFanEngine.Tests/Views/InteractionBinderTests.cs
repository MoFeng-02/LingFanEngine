using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Tests.Fakes;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// B1-d：InteractionBinder Tier A 测试（无宿主）。
/// <para>InteractionBinder 是 internal sealed，构造器 (IStateContainer, ICommandPipeline, ICommandService?)。
/// 覆盖 disabled 早退、ResolveValue（{}表达式点击时求值）、nav/cmd 点击行为。</para>
/// <para>雷点规避：nav/cmd 分支会 new Cursor(StandardCursorType.Hand)（需宿主服务定位器），
/// 故凡触发点击分支的用例均传入 cursor 属性以跳过 L134-135 的 Cursor 创建。
/// 点击通过 RaiseEvent(Button.ClickEvent) 在无宿主下触发（纯路由事件，不涉渲染）。</para>
/// </summary>
public class InteractionBinderTests
{
    private sealed class RecordingCommandService : ICommandService
    {
        public List<(string cmd, object? val)> Calls { get; } = new();

        public Task ExecuteAsync(string commandName, object? commandValue, CancellationToken ct = default)
        {
            Calls.Add((commandName, commandValue));
            return Task.CompletedTask;
        }

        public ValueTask SendCommandAsync(ICommand command, CancellationToken ct = default) => default;
        public void RegisterCommand(string commandName, Func<object?, CancellationToken, Task> handler) { }
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class => new Noop();
        public void Publish<TEvent>(TEvent evt) where TEvent : class { }
        public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class => Task.CompletedTask;
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private static InteractionBinder Create(
        out StateContainer state,
        out FakeCommandPipeline pipeline,
        RecordingCommandService? cmdService = null)
    {
        state = new StateContainer();
        pipeline = new FakeCommandPipeline();
        return new InteractionBinder(state, pipeline, cmdService);
    }

    // ========== ResolveValue（私有，反射调用）==========

    private static string? ResolveValue(InteractionBinder binder, string? raw)
    {
        var m = typeof(InteractionBinder).GetMethod("ResolveValue",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (string?)m.Invoke(binder, new object?[] { raw });
    }

    [Fact]
    public void ResolveValue_Null_ReturnsNull()
    {
        var binder = Create(out _, out _);
        ResolveValue(binder, null).Should().BeNull();
    }

    [Fact]
    public void ResolveValue_Empty_ReturnsEmpty()
    {
        var binder = Create(out _, out _);
        ResolveValue(binder, "").Should().Be("");
    }

    [Fact]
    public void ResolveValue_NoBrace_ReturnsRawUnchanged()
    {
        var binder = Create(out _, out _);
        ResolveValue(binder, "plain-text").Should().Be("plain-text");
    }

    [Fact]
    public void ResolveValue_Expression_EvaluatedAgainstState()
    {
        var binder = Create(out var state, out _);
        state.Set("score", 42);
        ResolveValue(binder, "{score}").Should().Be("42");
    }

    [Fact]
    public void ResolveValue_MixedExpression_Interpolated()
    {
        var binder = Create(out var state, out _);
        state.Set("hp", 100);
        ResolveValue(binder, "hp={hp}").Should().Be("hp=100");
    }

    // ========== disabled 早退 ==========

    [Fact]
    public void Disabled_PropTrue_SetsIsEnabledFalse()
    {
        var binder = Create(out _, out _);
        var btn = new Button { IsEnabled = true };
        binder.ApplyInteraction(btn, new() { ["disabled"] = true });
        btn.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Disabled_WithNav_EarlyReturns_NoCursorNoThrow()
    {
        // disabled 早退发生在 nav 处理（含 new Cursor(Hand)）之前，
        // 故即便带 nav 也不会触碰需宿主的 Cursor 创建——无宿主下不抛异常。
        var binder = Create(out _, out _);
        var btn = new Button();
        var act = () => binder.ApplyInteraction(btn, new()
        {
            ["disabled"] = true,
            ["nav"] = "/somewhere"
        });
        act.Should().NotThrow();
        btn.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void AlreadyDisabledControl_EarlyReturns()
    {
        var binder = Create(out _, out _);
        var btn = new Button { IsEnabled = false };
        binder.ApplyInteraction(btn, new() { ["nav"] = "/x" });
        btn.IsEnabled.Should().BeFalse();
    }

    // ========== nav 点击行为 ==========

    [Fact]
    public void Nav_ButtonClick_SendsNavigateCommand()
    {
        var binder = Create(out var state, out var pipeline);
        var btn = new Button();
        // 传 cursor 以跳过 new Cursor(Hand)（需宿主）
        binder.ApplyInteraction(btn, new() { ["nav"] = "/town", ["cursor"] = "hand" });

        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        pipeline.Sent.Should().ContainSingle();
        pipeline.Sent[0].Should().BeOfType<NavigateCommand>();
        ((NavigateCommand)pipeline.Sent[0]).Path.Should().Be("/town");
        state.Get<bool>(StateKeys.Dialog.Complete).Should().BeFalse();
    }

    // ========== cmd 点击行为（含 ResolveValue 点击时求值）==========

    [Fact]
    public void Cmd_ButtonClick_ExecutesWithResolvedValue()
    {
        var svc = new RecordingCommandService();
        var binder = Create(out var state, out _, svc);
        state.Set("score", 7);
        var btn = new Button();
        binder.ApplyInteraction(btn, new()
        {
            ["cmd"] = "add_score",
            ["value"] = "{score}",
            ["cursor"] = "hand"
        });

        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        svc.Calls.Should().ContainSingle();
        svc.Calls[0].cmd.Should().Be("add_score");
        svc.Calls[0].val.Should().Be("7");
    }

    [Fact]
    public void Cmd_ValueResolvedAtClickTime_NotBindTime()
    {
        var svc = new RecordingCommandService();
        var binder = Create(out var state, out _, svc);
        state.Set("score", 1);
        var btn = new Button();
        binder.ApplyInteraction(btn, new()
        {
            ["cmd"] = "set",
            ["value"] = "{score}",
            ["cursor"] = "hand"
        });

        // 绑定后改变量 → 点击时应取到最新值
        state.Set("score", 99);
        btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        svc.Calls[0].val.Should().Be("99");
    }

    [Fact]
    public void Cmd_NoCommandService_DoesNotThrowOnClick()
    {
        // cmdService=null → 点击分支 if(_cmdService != null) 保护，不抛
        var binder = Create(out var state, out _, null);
        state.Set("score", 5);
        var btn = new Button();
        binder.ApplyInteraction(btn, new()
        {
            ["cmd"] = "noop",
            ["value"] = "{score}",
            ["cursor"] = "hand"
        });

        var act = () => btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        act.Should().NotThrow();
    }
}
