using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// DebugLogHandler 集成测试：调试模式开关控制是否记录日志。
/// </summary>
public class DebugHandlersTests
{
    [Fact]
    public void DebugLogHandler_Disabled_DoesNotRecord()
    {
        var ctx = new FakeCommandContext();
        // Debug.Enabled 默认 false
        new DebugLogHandler().Handle(new DebugLogCommand { Message = "secret" }, ctx);
        ctx.State.Get<List<DebugLogEntry>>(StateKeys.Debug.Logs).Should().BeNull();
    }

    [Fact]
    public void DebugLogHandler_Enabled_RecordsLog()
    {
        var ctx = new FakeCommandContext();
        ctx.State.Set(StateKeys.Debug.Enabled, true);
        new DebugLogHandler().Handle(new DebugLogCommand { Message = "hello", Level = "Warning" }, ctx);
        var logs = ctx.State.Get<List<DebugLogEntry>>(StateKeys.Debug.Logs);
        logs.Should().ContainSingle();
        logs![0].Message.Should().Be("hello");
        logs[0].Level.Should().Be("Warning");
    }
}
