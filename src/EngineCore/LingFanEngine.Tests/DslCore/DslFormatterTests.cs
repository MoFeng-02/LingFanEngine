using FluentAssertions;
using LingFanEngine.Dsl.LanguageService;
using Xunit;

namespace LingFanEngine.Tests.DslCore;

/// <summary>
/// DslFormatter 纯缩进规整器单元测试——锁定「源文本保留式保守格式化」语义：
/// <list type="bullet">
///   <item>只重排缩进 + 去尾随空白，原样保留每行内容与行内注释（绝不丢信息）；</list>
///   <item><b>按域缩进</b>：普通体行（say/set/let/se 等）继承当前块深度，不根据自身缩进弹栈；</item>
///   <item><c>else</c>/<c>else if</c> 是续行：对齐到所属 <c>if</c> 同级（比体浅一层），不压栈；</item>
///   <item><c>case</c>/<c>default</c> 是开块：缩进到 <c>switch</c> 的 body 级，其体更深；</item>
///   <item>其余开块关键字（scene/if/while/for/func/switch/foreach/label/menu/set_time_event/popup）压栈使体更深。</item>
/// </list>
/// </summary>
public class DslFormatterTests
{
    [Fact]
    public void Format_KeepsAlreadyCorrectIndentation_Stable()
    {
        const string src = "scene a:\n  say \"hi\"\n  if {x}\n    say \"y\"\n  else\n    say \"z\"\n";
        var out1 = DslFormatter.Format(src);
        var out2 = DslFormatter.Format(out1);
        out1.Should().Be(src);
        out2.Should().Be(src); // 幂等
    }

    [Fact]
    public void Format_NormalizesInconsistentWidth_UsingExistingNesting()
    {
        // 真实可运行脚本：缩进层级正确，但用了 4 空格宽度。格式化应按「嵌套层数」归一化到 2 空格。
        const string messy =
            "scene a:\n" +
            "    say \"hi\"\n" +
            "    if {x}\n" +
            "        say \"y\"\n" +
            "    else\n" +
            "        say \"z\"\n";
        var expected =
            "scene a:\n" +
            "  say \"hi\"\n" +
            "  if {x}\n" +
            "    say \"y\"\n" +
            "  else\n" +
            "    say \"z\"\n";
        DslFormatter.Format(messy).Should().Be(expected);
    }

    [Fact]
    public void Format_ElseAlignsWithIf_NotDeeper()
    {
        // 真实约定：else 与 if 同列（比体浅一层）。
        const string src =
            "label t:\n" +
            "  if {a}\n" +
            "    say \"x\"\n" +
            "  else if {b}\n" +
            "    say \"y\"\n" +
            "  else\n" +
            "    say \"z\"\n" +
            "  say \"done\"\n";
        // 按域缩进：普通体行继承当前块深度，因此 "say done" 仍视为 if 块内的体行，
        // 缩进到与 if 体同级。若作者意图为 if 结束后语句，需用新块关键字或显式写在 if 体外。
        var expected =
            "label t:\n" +
            "  if {a}\n" +
            "    say \"x\"\n" +
            "  else if {b}\n" +
            "    say \"y\"\n" +
            "  else\n" +
            "    say \"z\"\n" +
            "    say \"done\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void Format_BodyIndentError_DoesNotBreakDomain()
    {
        // 用户真实场景：把 if 块内第一个 say 误删到 0 缩进，格式化应按域把它拉回 if 体，
        // 而不是根据这一行的缩进把整个 if 域破坏掉。
        const string messy =
            "if {_local_enemy_hp <= 0}\n" +
            "say \"野怪倒下！+30 经验，+25 金币\" speaker=\"系统\"\n" +
            "  set \"player.exp\" {player.exp + 30}\n" +
            "  set \"player.gold\" {player.gold + 25}\n" +
            "else\n" +
            "  set \"player.hp\" {player.hp - random(5, 15)}\n";
        var expected =
            "if {_local_enemy_hp <= 0}\n" +
            "  say \"野怪倒下！+30 经验，+25 金币\" speaker=\"系统\"\n" +
            "  set \"player.exp\" {player.exp + 30}\n" +
            "  set \"player.gold\" {player.gold + 25}\n" +
            "else\n" +
            "  set \"player.hp\" {player.hp - random(5, 15)}\n";
        DslFormatter.Format(messy).Should().Be(expected);
    }

    [Fact]
    public void Format_NestedIfBodyIndentError_DoesNotBreakDomain()
    {
        // 嵌套 if：外层 else 分支里的内层 if，其体行缩进错误时也应按域修复。
        const string messy =
            "label sb_battle:\n" +
            "  if {a}\n" +
            "    say \"x\"\n" +
            "  else\n" +
            "    if {b}\n" +
            "set \"y\" 1\n" +
            "    else\n" +
            "      say \"z\"\n";
        var expected =
            "label sb_battle:\n" +
            "  if {a}\n" +
            "    say \"x\"\n" +
            "  else\n" +
            "    if {b}\n" +
            "      set \"y\" 1\n" +
            "    else\n" +
            "      say \"z\"\n";
        DslFormatter.Format(messy).Should().Be(expected);
    }

    [Fact]
    public void Format_CaseDefaultIndentUnderSwitchBody()
    {
        // 真实约定（docs 示例）：case/default 缩进到 switch 的 body 级，比 switch 深一层。
        const string src =
            "switch {player.level}\n" +
            "  case 1\n" +
            "    say \"新手\"\n" +
            "  case 5\n" +
            "    say \"老手\"\n" +
            "  default\n" +
            "    say \"等级\"\n";
        var expected =
            "switch {player.level}\n" +
            "  case 1\n" +
            "    say \"新手\"\n" +
            "  case 5\n" +
            "    say \"老手\"\n" +
            "  default\n" +
            "    say \"等级\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void Format_NestedSwitchInsideIf_BodyLevelsCorrect()
    {
        const string src =
            "if {a}\n" +
            "  switch {b}\n" +
            "    case 1\n" +
            "      say \"x\"\n" +
            "    case 2\n" +
            "      say \"y\"\n";
        var expected =
            "if {a}\n" +
            "  switch {b}\n" +
            "    case 1\n" +
            "      say \"x\"\n" +
            "    case 2\n" +
            "      say \"y\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void Format_PreservesFullLineAndInlineComments()
    {
        const string src =
            "// 整行注释保持原样\n" +
            "# 另一种整行注释\n" +
            "scene a: // 行内注释\n" +
            "  say \"hi\" // 旁白\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    [Fact]
    public void Format_RemovesTrailingWhitespace_KeepsContent()
    {
        const string src = "scene a:   \n  say \"hi\"\t\n";
        const string expected = "scene a:\n  say \"hi\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void Format_TabSizeOption_RespectsUnit()
    {
        const string src = "scene a:\n  say \"hi\"\n";
        // tabSize=4 + insertSpaces → 每层 4 空格
        const string expected = "scene a:\n    say \"hi\"\n";
        DslFormatter.Format(src, tabSize: 4, insertSpaces: true).Should().Be(expected);
    }

    [Fact]
    public void Format_InsertTabs_WhenRequested()
    {
        const string src = "scene a:\n  say \"hi\"\n";
        const string expected = "scene a:\n\tsay \"hi\"\n"; // 每层 1 个 tab：depth 1 → 1 tab
        DslFormatter.Format(src, tabSize: 4, insertSpaces: false).Should().Be(expected);
    }

    [Fact]
    public void Format_TracksIndentAcrossBlankLines()
    {
        // 空行不破坏块栈：空行后语句仍按开块层级缩进。
        const string src = "if {a}\n  say \"x\"\n\n  say \"y\"\n";
        const string expected = "if {a}\n  say \"x\"\n\n  say \"y\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void FormatRange_OnlyReformatsSelectedLines()
    {
        // 第 1..3 行（0-based，闭区间）为范围内：这些行用 4 空格宽度，应归一化到 2 空格。
        // 范围外行原样保留（含其缩进），但预处理时仍参与缩进栈推导，使区间内 depth 正确。
        const string src =
            "scene a:\n" +
            "    say \"messy1\"\n" +
            "    if {x}\n" +
            "        say \"messy2\"\n" +
            "  else\n" +
            "    say \"z\"\n" +
            "untouched_line\n";
        var formatted = DslFormatter.FormatRange(src, 1, 3);
        // 范围内行被规整（4 空格 → 2 空格）：
        formatted.Should().Contain("  say \"messy1\"");
        formatted.Should().Contain("  if {x}");
        formatted.Should().Contain("    say \"messy2\"");
        // 范围外原样保留：
        formatted.Should().Contain("scene a:");
        formatted.Should().Contain("untouched_line");
        formatted.Should().Contain("  else");   // 第 4 行不在 [1,3]，保持原样
    }

    [Theory]
    [InlineData("label")]
    [InlineData("menu")]
    [InlineData("set_time_event")]
    [InlineData("popup")]
    [InlineData("func")]
    [InlineData("while")]
    [InlineData("for")]
    [InlineData("foreach")]
    public void Format_BlockStarter_IndentsBodyOneLevel(string keyword)
    {
        var src = $"{keyword} x:\n  body_cmd\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    // ── 花括号块语法 ──

    [Fact]
    public void Format_BracedIfBlock_PopsOnCloseBrace()
    {
        const string src =
            "if {x} {\n" +
            "  say \"in if\"\n" +
            "}\n" +
            "say \"after\"\n";
        var expected =
            "if {x} {\n" +
            "  say \"in if\"\n" +
            "}\n" +
            "say \"after\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void Format_BracedIfElseIf_PopsAndContinues()
    {
        const string src =
            "if {a} {\n" +
            "  say \"a\"\n" +
            "} else if {b} {\n" +
            "  say \"b\"\n" +
            "}\n" +
            "say \"done\"\n";
        var expected =
            "if {a} {\n" +
            "  say \"a\"\n" +
            "} else if {b} {\n" +
            "  say \"b\"\n" +
            "}\n" +
            "say \"done\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void Format_BracedIfElse_PopsAndContinues()
    {
        const string src =
            "if {a} {\n" +
            "  say \"a\"\n" +
            "} else {\n" +
            "  say \"b\"\n" +
            "}\n" +
            "say \"done\"\n";
        var expected =
            "if {a} {\n" +
            "  say \"a\"\n" +
            "} else {\n" +
            "  say \"b\"\n" +
            "}\n" +
            "say \"done\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void Format_BracedBlock_NestedInLabel()
    {
        const string src =
            "label t:\n" +
            "  if {x} {\n" +
            "    say \"hi\"\n" +
            "  }\n" +
            "  say \"after\"\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    [Fact]
    public void Format_BracedBlock_MixedWithIndented()
    {
        // 缩进式 if + 花括号式 if 混用
        const string src =
            "label t:\n" +
            "  if {a}\n" +
            "    say \"indent style\"\n" +
            "  if {b} {\n" +
            "    say \"brace style\"\n" +
            "  }\n" +
            "  say \"done\"\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    // ── scene 导航歧义 ──

    [Fact]
    public void Format_SceneInsideLabel_IsNavigation_NotBlockStarter()
    {
        const string src =
            "label sc_tour:\n" +
            "  say \"hi\"\n" +
            "  scene \"title_main\"\n" +
            "  say \"after scene\"\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    [Fact]
    public void Format_SceneAtTopLevel_IsBlockStarter()
    {
        const string src =
            "scene \"showcase\"\n" +
            "  say \"in scene\"\n" +
            "  text \"hello\"\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    [Fact]
    public void Format_SceneInsideIf_IsNavigation()
    {
        const string src =
            "if {x}\n" +
            "  scene \"inner\"\n" +
            "  say \"after\"\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    // ── end 关键字弹栈 ──

    [Fact]
    public void Format_EndKeyword_PopsStack()
    {
        const string src =
            "if {x}\n" +
            "  say \"in if\"\n" +
            "end\n" +
            "say \"after\"\n";
        var expected =
            "if {x}\n" +
            "  say \"in if\"\n" +
            "end\n" +
            "say \"after\"\n";
        DslFormatter.Format(src).Should().Be(expected);
    }

    [Fact]
    public void Format_EndKeyword_NestedBlocks()
    {
        const string src =
            "label t:\n" +
            "  if {a}\n" +
            "    say \"inner\"\n" +
            "  end\n" +
            "  say \"still in label\"\n" +
            "end\n" +
            "say \"top level\"\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    // ── 多层嵌套混合场景 ──

    [Fact]
    public void Format_ComplexNesting_SceneIfMenu()
    {
        const string src =
            "scene \"demo\"\n" +
            "  say \"welcome\"\n" +
            "  if {ready}\n" +
            "    say \"ready!\"\n" +
            "  menu \"choose\"\n" +
            "    option \"A\" -> path_a\n" +
            "    option \"B\" -> path_b\n" +
            "  say \"done\"\n";
        DslFormatter.Format(src).Should().Be(src);
    }

    [Fact]
    public void Format_MessyComplexNesting_NormalizesCorrectly()
    {
        const string messy =
            "scene \"demo\"\n" +
            "say \"welcome\"\n" +
            "  if {ready}\n" +
            "say \"ready!\"\n" +
            "  say \"done\"\n";
        var expected =
            "scene \"demo\"\n" +
            "  say \"welcome\"\n" +
            "  if {ready}\n" +
            "    say \"ready!\"\n" +
            "  say \"done\"\n";
        DslFormatter.Format(messy).Should().Be(expected);
    }
}
