using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>
/// DslExecutor 集成测试（Phase 3 主战场）。
/// 手工构造 ICommand 序列，经 EngineTestHost.RunDslAndDriveAsync 自动推进交互阻塞，
/// 验证命令执行、状态变更、if/分支、jump 等控制流与「发命令 + 等状态键」闭环。
/// </summary>
public class DslExecutorTests
{
    [Fact]
    public async Task PureControlFlow_RunsToEnd_AndSetsVariables()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "gold", Value = 10 },
            new SetVariableCommand { Key = "msg", Value = "hello" },
            new WaitCommand { Seconds = 0.01, IsSkipable = false },
        };

        await host.RunDslAndDriveAsync(cmds);

        host.DslExecutor.IsRunning.Should().BeFalse();
        host.State.Get<int>("gold").Should().Be(10);
        host.State.Get<string>("msg").Should().Be("hello");
    }

    [Fact]
    public async Task ConditionalBranch_FalseConditionSkipsBlock()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "gold", Value = 10 },
            // gold >= 50 为假 → 跳过后续 2 条（skippedA / skippedB）
            new BranchCommand { Condition = "gold >= 50", SkipCount = 2 },
            new SetVariableCommand { Key = "skippedA", Value = 1 },
            new SetVariableCommand { Key = "skippedB", Value = 1 },
            new SetVariableCommand { Key = "reached", Value = 1 },
        };

        await host.RunDslAndDriveAsync(cmds);

        host.State.ContainsKey("skippedA").Should().BeFalse();
        host.State.ContainsKey("skippedB").Should().BeFalse();
        host.State.Get<int>("reached").Should().Be(1);
        host.State.Get<int>("gold").Should().Be(10);
    }

    [Fact]
    public async Task ConditionalBranch_TrueConditionExecutesBlock()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "gold", Value = 10 },
            // gold >= 5 为真 → 顺序执行后续两条
            new BranchCommand { Condition = "gold >= 5", SkipCount = 2 },
            new SetVariableCommand { Key = "skippedA", Value = 1 },
            new SetVariableCommand { Key = "skippedB", Value = 1 },
            new SetVariableCommand { Key = "reached", Value = 1 },
        };

        await host.RunDslAndDriveAsync(cmds);

        host.State.Get<int>("skippedA").Should().Be(1);
        host.State.Get<int>("skippedB").Should().Be(1);
        host.State.Get<int>("reached").Should().Be(1);
    }

    [Fact]
    public async Task Jump_GoesToTargetIndex_SkippingIntermediate()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "v", Value = 0 },
            new JumpCommand { TargetIndex = 3 },          // 跳过 index 1
            new SetVariableCommand { Key = "v", Value = 1 }, // index 1，被跳过
            new SetVariableCommand { Key = "v", Value = 2 }, // index 2，落地
        };

        await host.RunDslAndDriveAsync(cmds);

        host.State.Get<int>("v").Should().Be(2);
    }

    [Fact]
    public void SayStatement_EscapedQuote_InnerText_ParsesCorrectly()
    {
        // 字符串转义：\" 在对白文本中还原为引号（VN 对白天然含引号）
        var stmt = LingFanEngine.DslCore.DslStatementParser.ParseLine("say \"他说\\\"你好\\\"\"");

        stmt.Should().NotBeNull();
        var say = stmt.Should().BeOfType<LingFanEngine.DslCore.SayStmt>().Subject;
        say.Text.Should().Be("他说\"你好\"");
    }

    [Fact]
    public void SayStatement_UnknownEscape_KeptVerbatim()
    {
        // 未知转义保留两字符原样——存量 Windows 路径 "C:\dir" 零破坏
        var stmt = LingFanEngine.DslCore.DslStatementParser.ParseLine("say \"路径 C:\\dir\"");

        stmt.Should().NotBeNull();
        var say = stmt.Should().BeOfType<LingFanEngine.DslCore.SayStmt>().Subject;
        say.Text.Should().Be("路径 C:\\dir");
    }

    [Fact]
    public async Task ConditionEvaluationFailure_TreatedAsFalse_ExecutionContinues()
    {
        // 语句级错误隔离：条件含类型错误（字符串与数字比较 "gold > \"a\""）时
        // 不得终止整个执行流——按 false 处理（走 else 分支），后续语句继续
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "gold", Value = 10 },
            // "gold > \"a\"" 求值抛 FormatException（ToDouble("a")）→ 按 false → 跳过 1 条
            new BranchCommand { Condition = "gold > \"a\"", SkipCount = 1 },
            new SetVariableCommand { Key = "inIfBody", Value = 1 },
            new SetVariableCommand { Key = "reached", Value = 1 },
        };

        await host.RunDslAndDriveAsync(cmds);

        host.State.ContainsKey("inIfBody").Should().BeFalse();
        host.State.Get<int>("reached").Should().Be(1);
        host.DslExecutor.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task SetExpressionEvaluationFailure_SkipsAssignment_ExecutionContinues()
    {
        // 语句级错误隔离：set 赋值表达式求值失败时跳过本次赋值（变量保持原值），不终止执行流
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "x", Value = 5 },
            // "a" > 1 求值抛 FormatException → 跳过赋值，x 保持 5
            new SetVariableCommand { Key = "x", Value = new DslExpressionPlaceholder("\"a\" > 1") },
            new SetVariableCommand { Key = "reached", Value = 1 },
        };

        await host.RunDslAndDriveAsync(cmds);

        host.State.Get<int>("x").Should().Be(5);
        host.State.Get<int>("reached").Should().Be(1);
        host.DslExecutor.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ShowDialogCommand_WithDriver_ReachesEnd()
    {
        var fake = new FakeCommandPipeline();
        var host = new EngineTestHost(pipeline: fake);
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "a", Value = 1 },
            new ShowDialogCommand { Text = "第一句" },
            new SetVariableCommand { Key = "b", Value = 2 },
        };

        await host.RunDslAndDriveAsync(cmds);

        host.DslExecutor.IsRunning.Should().BeFalse();
        host.State.Get<int>("b").Should().Be(2);
        fake.Sent.OfType<ShowDialogCommand>().Should().ContainSingle(c => c.Text == "第一句");
    }

    [Fact]
    public async Task MenuCommand_WithDriver_SelectsFirstOption_AndReachesEnd()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new MenuCommand
            {
                Prompt = "选择",
                Options = new List<(string, string)> { ("A", "lblA"), ("B", "lblB") }
            },
            new SetVariableCommand { Key = "after", Value = 1 },
        };

        await host.RunDslAndDriveAsync(cmds);

        host.DslExecutor.IsRunning.Should().BeFalse();
        host.State.Get<int>("after").Should().Be(1);
        host.State.Get<int>(StateKeys.Menu.Selected).Should().Be(-1); // 选择后被重置
    }

    [Fact]
    public async Task InputCommand_WithDriver_StoresResult_AndReachesEnd()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new InputCommand { Prompt = "名字", StoreKey = "player_name" },
            new SetVariableCommand { Key = "after", Value = 1 },
        };

        await host.RunDslAndDriveAsync(cmds);

        host.DslExecutor.IsRunning.Should().BeFalse();
        host.State.Get<string>("player_name").Should().Be("__dsl_input__");
    }

    [Fact]
    public async Task CallAndReturn_ExecutesSubroutine_AndResumesAfterCall()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "main_before", Value = 1 }, // 0
            new CallCommand { TargetLabel = "sub" },                   // 1 → 跳到 index 4，压栈返回点 2
            new SetVariableCommand { Key = "main_after", Value = 1 },  // 2 ← return 落点
            new EndCommand(),                                          // 3 提前终止，避免落入子过程
            new SetVariableCommand { Key = "in_sub", Value = 1 },      // 4 (label "sub")
            new ReturnCommand(),                                       // 5 弹栈 → 回到 index 2
        };
        var labels = new Dictionary<string, int> { ["sub"] = 4 };

        await host.RunDslAndDriveAsync(cmds, labels);

        host.DslExecutor.IsRunning.Should().BeFalse();
        host.State.Get<int>("main_before").Should().Be(1);
        host.State.Get<int>("in_sub").Should().Be(1);
        host.State.Get<int>("main_after").Should().Be(1);
    }

    [Fact]
    public async Task Return_WithEmptyCallStack_TerminatesExecution()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "a", Value = 1 }, // 0
            new ReturnCommand(),                             // 1 空调用栈 → 终止
            new SetVariableCommand { Key = "b", Value = 1 }, // 2 不可达
        };

        await host.RunDslAndDriveAsync(cmds);

        host.DslExecutor.IsRunning.Should().BeFalse();
        host.State.Get<int>("a").Should().Be(1);
        host.State.ContainsKey("b").Should().BeFalse();
    }

    [Fact]
    public async Task EndCommand_TerminatesEarly_SkippingRemaining()
    {
        var host = new EngineTestHost();
        var cmds = new List<ICommand>
        {
            new SetVariableCommand { Key = "a", Value = 1 }, // 0
            new EndCommand(),                                // 1 终止
            new SetVariableCommand { Key = "b", Value = 1 }, // 2 不可达
        };

        await host.RunDslAndDriveAsync(cmds);

        host.DslExecutor.IsRunning.Should().BeFalse();
        host.State.Get<int>("a").Should().Be(1);
        host.State.ContainsKey("b").Should().BeFalse();
    }
}
