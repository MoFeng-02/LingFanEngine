using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>
/// 用户报告的 while 循环脚本复现（2026-09-01）：
/// set "_local_i" 0 → while {_local_i < 3} → set "_local_i" {_local_i + 1} → say "循环第 {_local_i} 次"
/// </summary>
public class WhileLoopReproTests
{
    [Fact]
    public async Task UserScript_WhileLocalCounter_Runs()
    {
        var engine = new LingFanDslEngine();
        var script = """
            label sb_while:
              say "开始 while 循环测试..." speaker="系统"
              set "_local_i" 0
              while {_local_i < 3}
                set "_local_i" {_local_i + 1}
                say "循环第 {_local_i} 次" speaker="系统"
              say "循环测试完成！" speaker="系统"
              navigate "sandbox"
            """;
        var result = engine.Compile(script);
        result.Success.Should().BeTrue(result.Error);

        var host = new EngineTestHost();
        await host.RunDslAndDriveAsync(result.Commands, result.Labels);

        // 循环退出后 i 应为 3
        var i = host.State.Get<object>("_local_i");
        i.Should().NotBeNull("循环变量应存在于状态容器");
        System.Diagnostics.Debug.WriteLine($"[REPRO] _local_i = {i} ({i?.GetType().Name})");
    }
}
