using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>二分实验：定位带 filePath 编译时 while 循环死锁的根源</summary>
public class WhileBisectTests
{
    private static async Task<bool> RunAsync(string script, string? filePath)
    {
        var engine = new LingFanDslEngine();
        var result = engine.Compile(script, filePath);
        if (!result.Success) return false;
        var host = new EngineTestHost();
        var t = host.RunDslAndDriveAsync(result.Commands, result.Labels, timeoutMs: 2500);
        await t;
        var done = !host.DslExecutor.IsRunning;
        host.Dispose();
        return done;
    }

    [Fact]
    public async Task VariantA_FilePath_LocalVar_Hangs()
    {
        var ok = await RunAsync("""
            label t:
              set "_local_i" 0
              while {_local_i < 3}
                set "_local_i" {_local_i + 1}
                say "第 {_local_i} 次"
            """, "a.story");
        ok.Should().BeTrue("LocalScope.Read 已修复读写对称——带 filePath 的 _local_ 循环不再死锁");
    }

    [Fact]
    public async Task VariantB_FilePath_GlobalVar()
    {
        var ok = await RunAsync("""
            label t:
              set "gi" 0
              while {gi < 3}
                set "gi" {gi + 1}
                say "第 {gi} 次"
            """, "b.story");
        ok.Should().BeTrue("全局变量若不卡 → 死锁与 _local_ 作用域相关");
    }

    [Fact]
    public async Task VariantC_NoFilePath_LocalVar()
    {
        var ok = await RunAsync("""
            label t:
              set "_local_i" 0
              while {_local_i < 3}
                set "_local_i" {_local_i + 1}
                say "第 {_local_i} 次"
            """, null);
        ok.Should().BeTrue("无文件名若不卡 → 死锁与 SourceFile 注入相关");
    }

    [Fact]
    public async Task VariantD_FilePath_LocalVar_NoWhile()
    {
        // 对照：无 while，直接顺序执行 _local_ 变量 say——排除作用域读写本身
        var ok = await RunAsync("""
            label t:
              set "_local_i" 1
              say "第 {_local_i} 次"
            """, "d.story");
        ok.Should().BeTrue("无 while 的 _local_ 读写应正常");
    }
}
