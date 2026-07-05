using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

public class CommandPipelineTests
{
    [Fact]
    public async Task Send_Then_TryRead()
    {
        var pipeline = new CommandPipeline();
        var command = new TestCommand();

        await pipeline.SendAsync(command);
        var success = pipeline.TryRead(out var readCommand);

        success.Should().BeTrue();
        readCommand.Should().NotBeNull();
        readCommand.Should().Be(command);
    }

    [Fact]
    public void TryRead_Empty_ReturnsFalse()
    {
        var pipeline = new CommandPipeline();
        var success = pipeline.TryRead(out var command);

        success.Should().BeFalse();
        command.Should().BeNull();
    }

    [Fact]
    public async Task Order_Preserved()
    {
        var pipeline = new CommandPipeline();
        var commands = new TestCommand[3];

        for (var i = 0; i < commands.Length; i++)
        {
            commands[i] = new TestCommand { Id = i };
            await pipeline.SendAsync(commands[i]);
        }

        for (var i = 0; i < commands.Length; i++)
        {
            pipeline.TryRead(out var readCommand).Should().BeTrue();
            readCommand.Should().Be(commands[i]);
        }
    }

    [Fact]
    public void TimeScale_Default_1()
    {
        var pipeline = new CommandPipeline();
        pipeline.TimeScale.Should().Be(1.0f);
    }

    [Fact]
    public void TimeScale_CanBeChanged()
    {
        var pipeline = new CommandPipeline();
        pipeline.TimeScale = 2.0f;
        pipeline.TimeScale.Should().Be(2.0f);
    }

    [Fact]
    public void Count_Accurate()
    {
        var pipeline = new CommandPipeline();
        pipeline.Count.Should().Be(0);

        _ = pipeline.SendAsync(new TestCommand()).AsTask();
        pipeline.Count.Should().Be(1);

        _ = pipeline.SendAsync(new TestCommand()).AsTask();
        pipeline.Count.Should().Be(2);

        pipeline.TryRead(out _);
        pipeline.Count.Should().Be(1);

        pipeline.TryRead(out _);
        pipeline.Count.Should().Be(0);
    }

    [Fact]
    public async Task Complete_StopsAccepting()
    {
        var pipeline = new CommandPipeline();
        pipeline.Complete();

        var task = pipeline.SendAsync(new TestCommand());

        // Complete 后的 SendAsync 应该完成（成功或抛异常）
        await Task.WhenAny(task.AsTask(), Task.Delay(100));
        task.IsCompleted.Should().BeTrue();
    }

    // Test helper class
    private sealed class TestCommand : ICommand
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public CommandPriority Priority { get; set; } = CommandPriority.Normal;
        public int Id { get; set; }
    }
}