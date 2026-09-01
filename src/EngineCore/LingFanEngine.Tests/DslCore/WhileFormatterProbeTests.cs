using FluentAssertions;
using Xunit;

namespace LingFanEngine.Tests.DslCore;

/// <summary>用户 while 脚本的 DslFormatter 行为验证（SDK 编辑器格式化是否破坏缩进）</summary>
public class WhileFormatterProbeTests
{
    [Fact]
    public void Formatter_DoesNotBreakWhileLoopIndent()
    {
        var src = """
            label sb_while:
              say "开始 while 循环测试..." speaker="系统"
              set "_local_i" 0
              while {_local_i < 3}
                set "_local_i" {_local_i + 1}
                say "循环第 {_local_i} 次" speaker="系统"
              say "循环测试完成！" speaker="系统"
              navigate "sandbox"
            """;
        var formatted = LingFanEngine.Dsl.LanguageService.DslFormatter.Format(src);
        System.Diagnostics.Debug.WriteLine("[FMT-OUT]\n" + formatted);

        // 循环体（set/say）必须比 while 行更深缩进；循环后的 say 必须与 while 同级
        var lines = formatted.Replace("\r\n", "\n").Split('\n');
        var whileIdx = System.Array.FindIndex(lines, l => l.TrimStart().StartsWith("while"));
        whileIdx.Should().BeGreaterThanOrEqualTo(0, "while 行必须保留");

        var whileIndent = lines[whileIdx].Length - lines[whileIdx].TrimStart().Length;
        var bodySetIdx = System.Array.FindIndex(lines, l => l.TrimStart().StartsWith("set \"_local_i\" {_local_i"));
        bodySetIdx.Should().BeGreaterThan(whileIdx, "循环体必须在 while 之后");
        var bodyIndent = lines[bodySetIdx].Length - lines[bodySetIdx].TrimStart().Length;
        bodyIndent.Should().BeGreaterThan(whileIndent, "格式化后循环体缩进必须深于 while（否则循环体边界丢失）");

        var afterIdx = System.Array.FindIndex(lines, l => l.TrimStart().StartsWith("say \"循环测试完成"));
        afterIdx.Should().BeGreaterThan(whileIdx);
        var afterIndent = lines[afterIdx].Length - lines[afterIdx].TrimStart().Length;
        afterIndent.Should().Be(whileIndent, "循环后的 say 必须与 while 同级（否则被吞进循环体）");
    }
}
