using FluentAssertions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>
/// LingFanDslEngine 编译器测试
/// <para>覆盖所有 DSL 语句类型的编译结果正确性。</para>
/// </summary>
public class LingFanDslEngineTests
{
    private readonly LingFanDslEngine _engine = new();

    // ========== 基础语句 ==========

    [Fact]
    public void Compile_EmptyScript_ReturnsSuccess()
    {
        var result = _engine.Compile("");
        result.Success.Should().BeTrue();
        result.Commands.Should().BeEmpty();
    }

    [Fact]
    public void Compile_WhitespaceOnly_ReturnsSuccess()
    {
        var result = _engine.Compile("   \n  \n  ");
        result.Success.Should().BeTrue();
        result.Commands.Should().BeEmpty();
    }

    [Fact]
    public void Compile_CommentsAndBlankLines_AreSkipped()
    {
        var script = """
            // 这是注释
            # 这也是注释

            say "hello"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().HaveCount(1);
        result.Commands[0].Should().BeOfType<ShowDialogCommand>()
            .Which.Text.Should().Be("hello");
    }

    // ========== say 语句 ==========

    [Fact]
    public void Compile_Say_WithoutSpeaker()
    {
        var result = _engine.Compile("""say "你好世界" """);
        result.Success.Should().BeTrue();
        result.Commands.Should().HaveCount(1);
        var cmd = result.Commands[0].Should().BeOfType<ShowDialogCommand>().Subject;
        cmd.Text.Should().Be("你好世界");
        cmd.Speaker.Should().BeNull();
    }

    [Fact]
    public void Compile_Say_WithSpeaker()
    {
        var result = _engine.Compile("""say "你好" by "旅人" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowDialogCommand>().Subject;
        cmd.Text.Should().Be("你好");
        cmd.Speaker.Should().Be("旅人");
    }

    // ========== navigate 语句 ==========

    [Fact]
    public void Compile_Navigate_PathOnly()
    {
        var result = _engine.Compile("""navigate "chapter1" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<NavigateCommand>().Subject;
        cmd.Path.Should().Be("chapter1");
        cmd.SceneName.Should().BeNull();
    }

    [Fact]
    public void Compile_Navigate_WithSceneName()
    {
        var result = _engine.Compile("""navigate "chapter1" scene "intro" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<NavigateCommand>().Subject;
        cmd.Path.Should().Be("chapter1");
        cmd.SceneName.Should().Be("intro");
    }

    // ========== set / define / let 语句 ==========

    [Fact]
    public void Compile_Set_IntegerLiteral()
    {
        var result = _engine.Compile("""set "gold" 100 """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("gold");
        cmd.Value.Should().Be(100);
        cmd.IsDefine.Should().BeFalse();
    }

    [Fact]
    public void Compile_Set_FloatLiteral()
    {
        var result = _engine.Compile("""set "pi" 3.14 """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Value.Should().Be(3.14);
    }

    [Fact]
    public void Compile_Set_BooleanLiteral()
    {
        var result = _engine.Compile("""set "alive" true """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Value.Should().Be(true);
    }

    [Fact]
    public void Compile_Set_StringLiteral()
    {
        var result = _engine.Compile("""set "name" "Alice" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Value.Should().Be("Alice");
    }

    [Fact]
    public void Compile_Set_ExpressionPlaceholder()
    {
        var result = _engine.Compile("""set "gold" {gold + 50} """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Value.Should().BeOfType<DslExpressionPlaceholder>()
            .Which.Expression.Should().Be("gold + 50");
    }

    [Fact]
    public void Compile_Define_Once()
    {
        var result = _engine.Compile("""define "player.hp" 100 once """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("player.hp");
        cmd.Value.Should().Be(100);
        cmd.IsDefine.Should().BeTrue();
    }

    [Fact]
    public void Compile_Let_Once_AddsLocalPrefix()
    {
        var result = _engine.Compile("""let "temp" 42 once """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("_local_temp");
        cmd.Value.Should().Be(42);
        cmd.IsDefine.Should().BeTrue();
    }

    [Fact]
    public void Compile_Let_WithDot_ReplacesDotWithUnderscore()
    {
        var result = _engine.Compile("""let "player.x" 10 once """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("_local_player_x");
    }

    // ========== wait 语句 ==========

    [Fact]
    public void Compile_Wait()
    {
        var result = _engine.Compile("wait 2.0");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<WaitCommand>().Subject;
        cmd.Seconds.Should().Be(2.0);
    }

    // ========== bgm 语句 ==========

    [Fact]
    public void Compile_Bgm_DefaultVolume()
    {
        var result = _engine.Compile("""bgm "bgm/battle.mp3" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<PlayBgmCommand>().Subject;
        cmd.Path.Should().Be("bgm/battle.mp3");
        cmd.Volume.Should().Be(1.0f);
    }

    [Fact]
    public void Compile_Bgm_WithVolume()
    {
        var result = _engine.Compile("""bgm "bgm/battle.mp3" volume=0.8 """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<PlayBgmCommand>().Subject;
        cmd.Volume.Should().Be(0.8f);
    }

    // ========== transition 语句 ==========

    [Fact]
    public void Compile_Transition_DefaultDuration()
    {
        var result = _engine.Compile("""transition "fade" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<TransitionCommand>().Subject;
        cmd.Type.Should().Be("FadeIn");
        cmd.Duration.Should().Be(0.5);
    }

    [Fact]
    public void Compile_Transition_WithDuration()
    {
        var result = _engine.Compile("""transition "fade" duration=1.5 """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<TransitionCommand>().Subject;
        cmd.Duration.Should().Be(1.5);
    }

    [Fact]
    public void Compile_Transition_ZoomType()
    {
        var result = _engine.Compile("""transition "zoomin" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<TransitionCommand>().Subject;
        cmd.Type.Should().Be("ZoomIn");
    }

    // ========== show / hide / background 语句 ==========

    [Fact]
    public void Compile_Show_WithPosition()
    {
        var result = _engine.Compile("""show "alice" at (100, 200) """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowHideCommand>().Subject;
        cmd.Target.Should().Be("alice");
        cmd.X.Should().Be(100.0);
        cmd.Y.Should().Be(200.0);
        cmd.IsShow.Should().BeTrue();
        cmd.IsBackground.Should().BeFalse();
    }

    [Fact]
    public void Compile_Hide()
    {
        var result = _engine.Compile("""hide "alice" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowHideCommand>().Subject;
        cmd.Target.Should().Be("alice");
        cmd.IsShow.Should().BeFalse();
    }

    [Fact]
    public void Compile_Background()
    {
        var result = _engine.Compile("""background "bg/forest.jpg" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowHideCommand>().Subject;
        cmd.Target.Should().Be("bg/forest.jpg");
        cmd.IsShow.Should().BeTrue();
        cmd.IsBackground.Should().BeTrue();
    }

    // ========== animate 语句 ==========

    [Fact]
    public void Compile_Animate_DefaultDurationAndEasing()
    {
        var result = _engine.Compile("""animate "char1" opacity 0.0 """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<AnimateCommand>().Subject;
        cmd.Target.Should().Be("char1");
        cmd.Property.Should().Be("opacity");
        cmd.TargetValue.Should().Be(0.0);
        cmd.Duration.Should().Be(1.0);
        cmd.Easing.Should().Be("EaseOutQuad");
    }

    [Fact]
    public void Compile_Animate_WithDurationAndEasing()
    {
        var result = _engine.Compile("""animate "char1" x 500 duration=2.5 easing=Linear """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<AnimateCommand>().Subject;
        cmd.TargetValue.Should().Be(500.0);
        cmd.Duration.Should().Be(2.5);
        cmd.Easing.Should().Be("Linear");
    }

    // ========== input 语句 ==========

    [Fact]
    public void Compile_Input_WithoutOptions()
    {
        var result = _engine.Compile("""input "你叫什么？" store "player_name" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<InputCommand>().Subject;
        cmd.Prompt.Should().Be("你叫什么？");
        cmd.StoreKey.Should().Be("player_name");
        cmd.Options.Should().BeNull();
    }

    [Fact]
    public void Compile_Input_WithOptions()
    {
        var result = _engine.Compile("""input "选择" store "choice" options=["选项A","选项B"] """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<InputCommand>().Subject;
        cmd.Options.Should().NotBeNull();
        cmd.Options.Should().HaveCount(2);
        cmd.Options![0].Should().Be("选项A");
        cmd.Options![1].Should().Be("选项B");
    }

    // ========== save / load 语句 ==========

    [Fact]
    public void Compile_Save()
    {
        var result = _engine.Compile("""save "slot1" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SaveLoadCommand>().Subject;
        cmd.SlotId.Should().Be("slot1");
        cmd.IsSave.Should().BeTrue();
    }

    [Fact]
    public void Compile_Load()
    {
        var result = _engine.Compile("""load "slot1" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SaveLoadCommand>().Subject;
        cmd.SlotId.Should().Be("slot1");
        cmd.IsSave.Should().BeFalse();
    }

    // ========== scene / back / forward 语句 ==========

    [Fact]
    public void Compile_Scene()
    {
        var result = _engine.Compile("""scene "title_main" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SceneCommand>().Subject;
        cmd.SceneName.Should().Be("title_main");
    }

    [Fact]
    public void Compile_Back()
    {
        var result = _engine.Compile("back");
        result.Success.Should().BeTrue();
        result.Commands[0].Should().BeOfType<BackCommand>();
    }

    [Fact]
    public void Compile_Forward()
    {
        var result = _engine.Compile("forward");
        result.Success.Should().BeTrue();
        result.Commands[0].Should().BeOfType<ForwardCommand>();
    }

    // ========== label / jump / call / return ==========

    [Fact]
    public void Compile_Label_GeneratesLabelIndex()
    {
        var script = """
            label start:
            say "hello"
            jump start
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Labels.Should().NotBeNull();
        result.Labels!.Should().ContainKey("start");
    }

    [Fact]
    public void Compile_Jump_ForwardReference()
    {
        var script = """
            jump end_label
            label end_label:
            say "done"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Labels.Should().ContainKey("end_label");
    }

    [Fact]
    public void Compile_Call_Return()
    {
        var script = """
            call subroutine
            say "after call"
            label subroutine:
            say "in subroutine"
            return
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().Contain(c => c is CallCommand);
        result.Commands.Should().Contain(c => c is ReturnCommand);
    }

    // ========== if / else if / else 块 ==========

    [Fact]
    public void Compile_IfBlock_BranchCommandsGenerated()
    {
        var script = """
            if {gold >= 100} {
              say "rich"
            }
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().Contain(c => c is BranchCommand);
    }

    [Fact]
    public void Compile_IfElseIfElse_AllBranchesGenerated()
    {
        var script = """
            if {gold >= 100} {
              say "rich"
            } else if {gold >= 50} {
              say "medium"
            } else {
              say "poor"
            }
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        var branchCmds = result.Commands.Where(c => c is BranchCommand).ToList();
        branchCmds.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Compile_IfBlock_ConditionStripsBraces()
    {
        var script = """
            if {gold >= 100} {
              say "rich"
            }
            """;
        var result = _engine.Compile(script);
        var ifBranch = result.Commands.OfType<BranchCommand>().First();
        ifBranch.Condition.Should().Be("gold >= 100");
    }

    // ========== while 块 ==========

    [Fact]
    public void Compile_WhileBlock_BranchCommandGenerated()
    {
        var script = """
            while {hp > 0} {
              say "alive"
            }
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().Contain(c => c is BranchCommand);
        var whileBranch = result.Commands.OfType<BranchCommand>().First();
        whileBranch.Condition.Should().Be("hp > 0");
    }

    // ========== 多 label 场景 ==========

    [Fact]
    public void Compile_MultipleLabels_EachGetsEndCommand()
    {
        var script = """
            label start:
            say "start"
            label middle:
            say "middle"
            label end:
            say "end"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Labels.Should().NotBeNull();
        result.Labels!.Keys.Should().Contain(new[] { "start", "middle", "end" });
        result.Commands.Should().Contain(c => c is EndCommand);
    }

    [Fact]
    public void Compile_Jump_GetsTargetIndexFromLabel()
    {
        var script = """
            jump target
            label target:
            say "reached"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        var jumpCmd = result.Commands.OfType<JumpCommand>().FirstOrDefault();
        jumpCmd.Should().NotBeNull();
        jumpCmd!.TargetLabel.Should().Be("target");
        jumpCmd.TargetIndex.Should().BeGreaterThanOrEqualTo(0);
    }

    // ========== menu 语句 ==========

    [Fact]
    public void Compile_Menu_GeneratesMenuCommand()
    {
        var result = _engine.Compile("""menu "选择你的道路" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<MenuCommand>().Subject;
        cmd.Prompt.Should().Be("选择你的道路");
    }

    // ========== 复合脚本 ==========

    [Fact]
    public void Compile_FullScript_AllCommandsPresent()
    {
        var script = """
            label start:
            say "欢迎来到游戏" by "旁白"
            set "gold" 100
            define "player.name" "勇者" once
            bgm "bgm/intro.mp3" volume=0.9
            wait 1.5
            transition "fade" duration=1.0
            if {gold >= 50} {
              say "你很富有"
            } else {
              say "你需要更多金币"
            }
            navigate "chapter1"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().NotBeEmpty();
        result.Labels.Should().ContainKey("start");

        var cmds = result.Commands.Where(c => c is not EndCommand && c is not BranchCommand).ToList();
        cmds.Should().Contain(c => c is ShowDialogCommand);
        cmds.Should().Contain(c => c is SetVariableCommand);
        cmds.Should().Contain(c => c is PlayBgmCommand);
        cmds.Should().Contain(c => c is WaitCommand);
        cmds.Should().Contain(c => c is TransitionCommand);
        cmds.Should().Contain(c => c is NavigateCommand);
    }

    [Fact]
    public async Task CompileAsync_ReturnsSameResult()
    {
        var script = """say "hello" """;
        var syncResult = _engine.Compile(script);
        var asyncResult = await _engine.CompileAsync(script);
        asyncResult.Success.Should().Be(syncResult.Success);
        asyncResult.Commands.Should().HaveCount(syncResult.Commands.Count);
    }
}
