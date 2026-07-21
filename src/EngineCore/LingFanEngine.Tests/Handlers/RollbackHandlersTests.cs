using FluentAssertions;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// RollbackHandlers 集成测试：时间线回退/前进/跳转到检查点。
/// </summary>
public class RollbackHandlersTests
{
    [Fact]
    public void RollbackHandler_CallsDslExecutorRollback_WhenCanRollback()
    {
        var exe = new FakeDslExecutor { CanRollbackResult = true };
        var ctx = new FakeCommandContext { DslExecutor = exe };
        new RollbackHandler().Handle(new RollbackCommand(), ctx);
        exe.RollbackCount.Should().Be(1);
    }

    [Fact]
    public void RollbackHandler_DoesNotCall_WhenCannotRollback()
    {
        var exe = new FakeDslExecutor { CanRollbackResult = false };
        var ctx = new FakeCommandContext { DslExecutor = exe };
        new RollbackHandler().Handle(new RollbackCommand(), ctx);
        exe.RollbackCount.Should().Be(0);
    }

    [Fact]
    public void RollforwardHandler_CallsDslExecutorRollforward()
    {
        var exe = new FakeDslExecutor { CanRollforwardResult = true };
        var ctx = new FakeCommandContext { DslExecutor = exe };
        new RollforwardHandler().Handle(new RollforwardCommand(), ctx);
        exe.RollforwardCount.Should().Be(1);
    }

    [Fact]
    public void RollbackToHandler_NegativeIndex_EqualsRollback()
    {
        var exe = new FakeDslExecutor();
        var ctx = new FakeCommandContext { DslExecutor = exe };
        new RollbackToHandler().Handle(new RollbackToCommand { TargetCheckpointIndex = -1 }, ctx);
        exe.RollbackCount.Should().Be(1);
        exe.RollbackToCount.Should().Be(0);
    }

    [Fact]
    public void RollbackToHandler_PositiveIndex_CallsRollbackTo()
    {
        var exe = new FakeDslExecutor();
        var ctx = new FakeCommandContext { DslExecutor = exe };
        new RollbackToHandler().Handle(new RollbackToCommand { TargetCheckpointIndex = 2 }, ctx);
        exe.RollbackToCount.Should().Be(1);
    }
}
