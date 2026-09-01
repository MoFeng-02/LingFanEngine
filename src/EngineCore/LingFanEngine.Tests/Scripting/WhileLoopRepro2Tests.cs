using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>
/// 用户 while 循环复现 #2：带 filePath 编译（真实游戏环境——LocalScope 作用域键参与）。
/// 根因：LocalScope.Read 不剥 _local_ 前缀，与 Write 不对称——读恒 null → 插值空
/// （"循环第 次"）+ null&lt;3 恒真 → 死循环卡死。已修复 Read 与 Write 对称。
/// </summary>
public class WhileLoopRepro2Tests
{
    [Fact]
    public async Task UserScript_WithFilePath_LoopTerminates()
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
        var result = engine.Compile(script, "sandbox.story");
        result.Success.Should().BeTrue(result.Error);

        var host = new EngineTestHost();
        await host.RunDslAndDriveAsync(result.Commands, result.Labels);

        // 循环正常退出：i 递增到 3（label 级作用域键）
        host.DslExecutor.IsRunning.Should().BeFalse("循环必须终止（修复前 null<3 恒真死循环）");
        var i = host.State.Get<object>("_local_L_sandbox_story_sb_while_i");
        i.Should().NotBeNull("作用域键应存在");
        System.Convert.ToDouble(i).Should().Be(3, "i 应递增到 3 后退出（修复前恒为 1：null+1 每轮重写）");
    }
}
