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

    // ========== voice 语句 ==========

    [Fact]
    public void Compile_Voice_DefaultVolume()
    {
        var result = _engine.Compile("""voice "voice/line1.wav" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<PlayVoiceCommand>().Subject;
        cmd.Path.Should().Be("voice/line1.wav");
        cmd.Volume.Should().Be(1.0f);
        cmd.AutoStop.Should().BeNull();
    }

    [Fact]
    public void Compile_Voice_WithVolumeAndAutoStop()
    {
        var result = _engine.Compile("""voice "voice/line1.wav" volume=0.9 auto_stop=false """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<PlayVoiceCommand>().Subject;
        cmd.Path.Should().Be("voice/line1.wav");
        cmd.Volume.Should().Be(0.9f);
        cmd.AutoStop.Should().BeFalse();
    }

    [Fact]
    public void Compile_StopVoice()
    {
        var result = _engine.Compile("""stop_voice """);
        result.Success.Should().BeTrue();
        result.Commands[0].Should().BeOfType<StopVoiceCommand>();
    }

    [Fact]
    public void Compile_Say_WithVoice()
    {
        var result = _engine.Compile("""say "你好" by "旅人" voice="voice/hi.wav" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowDialogCommand>().Subject;
        cmd.Text.Should().Be("你好");
        cmd.Speaker.Should().Be("旅人");
        cmd.VoicePath.Should().Be("voice/hi.wav");
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
            if {gold >= 100}
              say "rich"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().Contain(c => c is BranchCommand);
    }

    [Fact]
    public void Compile_IfElseIfElse_AllBranchesGenerated()
    {
        var script = """
            if {gold >= 100}
              say "rich"
            else if {gold >= 50}
              say "medium"
            else
              say "poor"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        var branchCmds = result.Commands.Where(c => c is BranchCommand).ToList();
        branchCmds.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Compile_IfElse_SkipCount_IncludesJumpCommand()
    {
        // BUG 回归测试：if 条件为 false 时必须跳到 else body，不能跳到 JumpCommand
        var script = """
            if {cond}
              say "true"
            else
              say "false"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();

        var branchCmd = result.Commands.OfType<BranchCommand>().First();
        var jumpCmd = result.Commands.OfType<JumpCommand>().FirstOrDefault();
        jumpCmd.Should().NotBeNull("if body 后应有 JumpCommand 跳到 endIf");

        // SkipCount 应包含 body + JumpCommand
        // 布局: [0]BranchCommand [1]ShowDialog("true") [2]JumpCommand [3]ShowDialog("false")
        // BranchCommand.SkipCount = 2 (body=1 + JumpCommand=1)
        // false 时: 0 + 2 + 1 = 3 → ShowDialog("false")（else body）
        branchCmd.SkipCount.Should().Be(2, "SkipCount 必须包含 JumpCommand，否则 else 分支不可达");
    }

    [Fact]
    public void Compile_IfElseIf_SkipCount_IncludesJumpCommand()
    {
        // BUG 回归测试：if 条件为 false 时必须跳到 else-if 的 BranchCommand
        var script = """
            if {cond1}
              say "a"
            else if {cond2}
              say "b"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();

        var branchCmds = result.Commands.OfType<BranchCommand>().ToList();
        branchCmds.Should().HaveCount(2);

        // 布局: [0]BranchCommand(cond1) [1]ShowDialog("a") [2]JumpCommand [3]BranchCommand(cond2) [4]ShowDialog("b")
        // BranchCommand[0].SkipCount = 2 (body=1 + JumpCommand=1)
        // false 时: 0 + 2 + 1 = 3 → BranchCommand(cond2)（else-if）
        branchCmds[0].SkipCount.Should().Be(2, "if 的 SkipCount 必须包含 JumpCommand，否则 else-if 不可达");
    }

    [Fact]
    public void Compile_IfBlock_ConditionStripsBraces()
    {
        var script = """
            if {gold >= 100}
              say "rich"
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
            while {hp > 0}
              say "alive"
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
            if {gold >= 50}
              say "你很富有"
            else
              say "你需要更多金币"
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
        var asyncResult = await _engine.CompileAsync(script, TestContext.Current.CancellationToken);
        asyncResult.Success.Should().Be(syncResult.Success);
        asyncResult.Commands.Should().HaveCount(syncResult.Commands.Count);
    }

    // ========== say speaker= 语法统一性 ==========

    [Fact]
    public void Compile_Say_WithSpeakerEqualsSyntax()
    {
        var result = _engine.Compile("""say "你好" speaker="旅人" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowDialogCommand>().Subject;
        cmd.Text.Should().Be("你好");
        cmd.Speaker.Should().Be("旅人");
    }

    [Fact]
    public void Compile_Say_WithBySyntax_CompatibilityAlias()
    {
        var result = _engine.Compile("""say "你好" by "旅人" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowDialogCommand>().Subject;
        cmd.Speaker.Should().Be("旅人");
    }

    [Fact]
    public void Compile_Say_WithClickableTrue()
    {
        var result = _engine.Compile("""say "点击按钮" clickable=true """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowDialogCommand>().Subject;
        cmd.Clickable.Should().BeTrue();
    }

    [Fact]
    public void Compile_Say_WithOkeySyntaxSugar()
    {
        var result = _engine.Compile("""say "点击按钮" okey """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShowDialogCommand>().Subject;
        cmd.Clickable.Should().BeTrue();
    }

    // ========== character 语句 ==========

    [Fact]
    public void Compile_Character_WithProperties()
    {
        var result = _engine.Compile("""character "boss" name="魔王" color="#FF4444" font="Microsoft YaHei" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("__char_boss");
        cmd.Value.Should().BeOfType<Dictionary<string, object?>>()
            .Which.Should().ContainKey("name");
    }

    // ========== style 语句（集中样式表）==========

    [Fact]
    public void Compile_Style_WithProperties()
    {
        var result = _engine.Compile("""style "btn_primary" color="#88CCFF" size=18 """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("__style_btn_primary");
        cmd.Value.Should().BeOfType<Dictionary<string, object?>>()
            .Which.Should().ContainKey("color");
    }

    [Fact]
    public void Compile_Style_MultipleProperties()
    {
        var result = _engine.Compile("""style "title" color="#FFD700" size=36 font="Microsoft YaHei" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        var dict = cmd.Value.Should().BeOfType<Dictionary<string, object?>>().Subject;
        dict.Should().HaveCount(3);
        dict.Should().ContainKey("color");
        dict.Should().ContainKey("size");
        dict.Should().ContainKey("font");
    }

    // ========== animate_block 语句（批量动画）==========

    [Fact]
    public void Compile_AnimateBlock_MultipleProperties()
    {
        var result = _engine.Compile("""animate_block "char1" x=100 y=200 opacity=0.5 duration=1.0 easing="EaseOutQuad" """);
        result.Success.Should().BeTrue();
        var animCmds = result.Commands.OfType<AnimateCommand>().ToList();
        animCmds.Should().HaveCount(3);
        animCmds[0].Target.Should().Be("char1");
        animCmds[0].Property.Should().Be("x");
        animCmds[0].TargetValue.Should().Be(100.0);
        animCmds[1].Property.Should().Be("y");
        animCmds[1].TargetValue.Should().Be(200.0);
        animCmds[2].Property.Should().Be("opacity");
        animCmds[2].TargetValue.Should().Be(0.5);
        // 所有动画共享 duration 和 easing
        foreach (var cmd in animCmds)
        {
            cmd.Duration.Should().Be(1.0);
            cmd.Easing.Should().Be("EaseOutQuad");
        }
    }

    [Fact]
    public void Compile_AnimateBlock_SingleProperty()
    {
        var result = _engine.Compile("""animate_block "char1" x=500 duration=2.0 """);
        result.Success.Should().BeTrue();
        var animCmds = result.Commands.OfType<AnimateCommand>().ToList();
        animCmds.Should().HaveCount(1);
        animCmds[0].TargetValue.Should().Be(500.0);
        animCmds[0].Duration.Should().Be(2.0);
    }

    [Fact]
    public void Compile_AnimateBlock_DefaultDurationAndEasing()
    {
        var result = _engine.Compile("""animate_block "char1" x=100 y=200 """);
        result.Success.Should().BeTrue();
        var animCmds = result.Commands.OfType<AnimateCommand>().ToList();
        animCmds.Should().HaveCount(2);
        foreach (var cmd in animCmds)
        {
            cmd.Duration.Should().Be(1.0);
            cmd.Easing.Should().Be("EaseOutQuad");
        }
    }

    // ========== call_screen 语句 ==========

    [Fact]
    public void Compile_CallScreen_WithoutStore()
    {
        var result = _engine.Compile("""call_screen "settings_panel" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<CallScreenCommand>().Subject;
        cmd.SceneName.Should().Be("settings_panel");
        cmd.StoreKey.Should().BeNull();
    }

    [Fact]
    public void Compile_CallScreen_WithStore()
    {
        var result = _engine.Compile("""call_screen "item_select" store="selected_item" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<CallScreenCommand>().Subject;
        cmd.SceneName.Should().Be("item_select");
        cmd.StoreKey.Should().Be("selected_item");
    }

    // ========== wait skipable 语句 ==========

    [Fact]
    public void Compile_Wait_Skipable()
    {
        var result = _engine.Compile("wait 3.0 skipable");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<WaitCommand>().Subject;
        cmd.Seconds.Should().Be(3.0);
        cmd.IsSkipable.Should().BeTrue();
    }

    [Fact]
    public void Compile_Wait_NotSkipable_ByDefault()
    {
        var result = _engine.Compile("wait 3.0");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<WaitCommand>().Subject;
        cmd.IsSkipable.Should().BeFalse();
    }

    // ========== pause 语句 ==========

    [Fact]
    public void Compile_Pause_NoArgs_WaitsForClick()
    {
        var result = _engine.Compile("pause");
        result.Success.Should().BeTrue();
        result.Commands[0].Should().BeOfType<HardPauseCommand>();
    }

    [Fact]
    public void Compile_Pause_WithSeconds_Skipable()
    {
        var result = _engine.Compile("pause 2.0");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<WaitCommand>().Subject;
        cmd.Seconds.Should().Be(2.0);
        cmd.IsSkipable.Should().BeTrue();
    }

    [Fact]
    public void Compile_Pause_WithSeconds_Hard()
    {
        var result = _engine.Compile("pause 2.0 hard");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<WaitCommand>().Subject;
        cmd.Seconds.Should().Be(2.0);
        cmd.IsSkipable.Should().BeFalse();
    }

    // ========== shake / skip / auto / nvl / debug 语句 ==========

    [Fact]
    public void Compile_Shake_DefaultValues()
    {
        var result = _engine.Compile("shake");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShakeCommand>().Subject;
        cmd.Intensity.Should().Be(10.0);
        cmd.Duration.Should().Be(0.5);
    }

    [Fact]
    public void Compile_Shake_WithParams()
    {
        var result = _engine.Compile("shake intensity=20 duration=1.0");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<ShakeCommand>().Subject;
        cmd.Intensity.Should().Be(20.0);
        cmd.Duration.Should().Be(1.0);
    }

    [Fact]
    public void Compile_Skip()
    {
        var result = _engine.Compile("skip");
        result.Success.Should().BeTrue();
        result.Commands[0].Should().BeOfType<ToggleSkipCommand>();
    }

    [Fact]
    public void Compile_Auto()
    {
        var result = _engine.Compile("auto");
        result.Success.Should().BeTrue();
        result.Commands[0].Should().BeOfType<ToggleAutoCommand>();
    }

    [Fact]
    public void Compile_Nvl_Clear()
    {
        var result = _engine.Compile("nvl clear");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<NvlCommand>().Subject;
        cmd.IsClear.Should().BeTrue();
    }

    [Fact]
    public void Compile_Debug_WithLevel()
    {
        var result = _engine.Compile("""debug "test message" level=Warning """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<DebugLogCommand>().Subject;
        cmd.Message.Should().Be("test message");
        cmd.Level.Should().Be("Warning");
    }

    // ========== gallery unlock 语句 ==========

    [Fact]
    public void Compile_GalleryUnlock_WithTitleAndScene()
    {
        var result = _engine.Compile("""gallery unlock "cg01" "images/cg01.jpg" title="回忆一" scene="chapter1" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<UnlockGalleryCommand>().Subject;
        cmd.Id.Should().Be("cg01");
        cmd.ImagePath.Should().Be("images/cg01.jpg");
        cmd.Title.Should().Be("回忆一");
        cmd.SceneName.Should().Be("chapter1");
    }

    // ========== 复合脚本：样式 + 元素 + 动画 ==========

    [Fact]
    public void Compile_StyleAndElement_StyleStoredAsVariable()
    {
        var script = """
            style "btn_primary" color="#88CCFF" size=18
            button "开始游戏" class="btn_primary" x=50 y=100
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Commands[0].Should().BeOfType<SetVariableCommand>();
        result.Commands.Should().Contain(c => c is ShowElementCommand);
    }

    [Fact]
    public void Compile_AnimateBlockInIfBlock()
    {
        var script = """
            if {moving}
              animate_block "char1" x=100 y=200 duration=0.5
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().Contain(c => c is BranchCommand);
        var animCmds = result.Commands.OfType<AnimateCommand>().ToList();
        animCmds.Should().HaveCount(2);
    }

    // ========== Phase 24: for 循环 ==========

    [Fact]
    public void Compile_ForBlock_BasicIteration()
    {
        var script = """
            for "item" in {items}
              say {item}
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // for 编译为：SetVariable(idx=0) + SetVariable(len) + Branch + SetVariable(var) + body + SetVariable(idx+1) + Jump + 清理
        var setCmds = result.Commands.OfType<SetVariableCommand>().ToList();
        setCmds.Should().HaveCountGreaterThanOrEqualTo(4, "for 块至少生成 idx/len 初始化 + var 赋值 + idx 递增 + 清理");
        // 应包含 BranchCommand（条件检查）
        result.Commands.Should().Contain(c => c is BranchCommand);
        // 应包含 JumpCommand（循环回条件检查）
        result.Commands.Should().Contain(c => c is JumpCommand);
    }

    [Fact]
    public void Compile_ForBlock_NestedFor()
    {
        var script = """
            for "i" in {outer}
              for "j" in {inner}
                say {j}
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // 嵌套 for 应有两组 BranchCommand
        var branchCmds = result.Commands.OfType<BranchCommand>().ToList();
        branchCmds.Should().HaveCount(2, "嵌套 for 应有两个条件检查 BranchCommand");
        // 应有两个 JumpCommand（内外各一个循环回跳）
        var jumpCmds = result.Commands.OfType<JumpCommand>().ToList();
        jumpCmds.Should().HaveCount(2, "嵌套 for 应有两个循环回跳 JumpCommand");
    }

    // ========== Phase 24: window 语句 ==========

    [Fact]
    public void Compile_Window_Auto()
    {
        var result = _engine.Compile("window auto");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("__dialog_window_mode");
        cmd.Value.Should().Be("auto");
    }

    [Fact]
    public void Compile_Window_Show()
    {
        var result = _engine.Compile("window show");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("__dialog_window_mode");
        cmd.Value.Should().Be("show");
    }

    [Fact]
    public void Compile_Window_Hide()
    {
        var result = _engine.Compile("window hide");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("__dialog_window_mode");
        cmd.Value.Should().Be("hide");
    }

    // ========== Phase 24: block_rollback / fix_rollback ==========

    [Fact]
    public void Compile_BlockRollback_GeneratesSetVariable()
    {
        var result = _engine.Compile("block_rollback");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("__rollback_blocked_until");
        cmd.Value.Should().BeOfType<int>().Which.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Compile_FixRollback_ClearsBlockedUntil()
    {
        var result = _engine.Compile("fix_rollback");
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("__rollback_blocked_until");
        cmd.Value.Should().Be(-1);
    }

    // ========== Phase 24: call_screen 带参数 ==========

    [Fact]
    public void Compile_CallScreen_WithParams()
    {
        var result = _engine.Compile("""call_screen "shop" with "gold=100,category=weapon" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<CallScreenCommand>().Subject;
        cmd.SceneName.Should().Be("shop");
        cmd.Params.Should().NotBeNull();
        cmd.Params.Should().ContainKey("gold");
        cmd.Params.Should().ContainKey("category");
    }

    // ========== Phase 24: character 侧脸图 ==========

    [Fact]
    public void Compile_Character_WithSideImage()
    {
        var result = _engine.Compile("""character "eileen" name="艾琳" side="img/eileen_happy.png" """);
        result.Success.Should().BeTrue();
        var cmd = result.Commands[0].Should().BeOfType<SetVariableCommand>().Subject;
        cmd.Key.Should().Be("__char_eileen");
        var dict = cmd.Value.Should().BeOfType<Dictionary<string, object?>>().Subject;
        dict.Should().ContainKey("side");
        dict["side"].Should().Be("img/eileen_happy.png");
    }

    // ========== Phase 25: show/hide with transition ==========

    [Fact]
    public void Compile_Show_WithTransition()
    {
        var result = _engine.Compile("""show "bg_school" with "fade" duration=1.5""");
        result.Success.Should().BeTrue();
        // 应生成 TransitionCommand + ShowHideCommand 两条命令
        var transitionCmd = result.Commands.OfType<TransitionCommand>().FirstOrDefault();
        transitionCmd.Should().NotBeNull();
        transitionCmd!.Type.Should().Be("FadeIn");
        transitionCmd.Duration.Should().Be(1.5);
        var showCmd = result.Commands.OfType<ShowHideCommand>().FirstOrDefault();
        showCmd.Should().NotBeNull();
        showCmd!.Target.Should().Be("bg_school");
        showCmd.IsShow.Should().BeTrue();
// TransitionCommand 应在 ShowHideCommand 之后（先显示元素，再启动过渡效果）
var tcIdx = -1; var scIdx = -1;
for (int i = 0; i < result.Commands.Count; i++)
{
if (result.Commands[i] is TransitionCommand && tcIdx < 0) tcIdx = i;
if (result.Commands[i] is ShowHideCommand && scIdx < 0) scIdx = i;
}
scIdx.Should().BeLessThan(tcIdx);
    }

    [Fact]
    public void Compile_Hide_WithTransition()
    {
        var result = _engine.Compile("""hide "bg_school" with "dissolve" duration=2.0""");
        result.Success.Should().BeTrue();
        var transitionCmd = result.Commands.OfType<TransitionCommand>().FirstOrDefault();
        transitionCmd.Should().NotBeNull();
        transitionCmd!.Duration.Should().Be(2.0);
        var hideCmd = result.Commands.OfType<ShowHideCommand>().FirstOrDefault();
        hideCmd.Should().NotBeNull();
        hideCmd!.Target.Should().Be("bg_school");
        hideCmd.IsShow.Should().BeFalse();
var tcIdx2 = -1; var hcIdx2 = -1;
for (int i = 0; i < result.Commands.Count; i++)
{
if (result.Commands[i] is TransitionCommand && tcIdx2 < 0) tcIdx2 = i;
if (result.Commands[i] is ShowHideCommand && hcIdx2 < 0) hcIdx2 = i;
}
hcIdx2.Should().BeLessThan(tcIdx2);
    }

    [Fact]
    public void Compile_Show_WithoutTransition_NoTransitionCommand()
    {
        var result = _engine.Compile("""show "bg_school" """);
        result.Success.Should().BeTrue();
        // 不带 with 过渡时不应生成 TransitionCommand
        result.Commands.Should().NotContain(c => c is TransitionCommand);
        var showCmd = result.Commands[0].Should().BeOfType<ShowHideCommand>().Subject;
        showCmd.Target.Should().Be("bg_school");
    }

    // ========== Phase 25: break / continue ==========

    [Fact]
    public void Compile_BreakInWhile_GeneratesJumpToEnd()
    {
        var script = """
            while {hp > 0}
              break
              say "never reached"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // break 编译为 JumpCommand
        result.Commands.Should().Contain(c => c is JumpCommand);
        // say 不应被跳过——但 break 之后的 say 仍编译（运行时不会执行到）
        result.Commands.Should().Contain(c => c is ShowDialogCommand);
    }

    [Fact]
    public void Compile_ContinueInFor_GeneratesJumpToIncrement()
    {
        var script = """
            for "i" in {items}
              continue
              say "never reached"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().Contain(c => c is JumpCommand);
    }

    [Fact]
    public void Compile_ContinueInWhile_JumpTargetResolves()
    {
        var script = """
            while {hp > 0}
              continue
              say "never reached"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // 验证所有 JumpCommand 的 TargetIndex 都已解析（>= 0）
        // BUG#1 修复前：continue 的 TargetIndex 为 -1（labels 未注册）
        var allJumps = result.Commands.OfType<JumpCommand>().ToList();
        allJumps.Should().NotBeEmpty();
        foreach (var jmp in allJumps)
        {
            jmp.TargetIndex.Should().BeGreaterThanOrEqualTo(0,
                "所有 JumpCommand 的 TargetIndex 必须被解析，continue 的 TargetIndex 不应为 -1");
        }
    }

    [Fact]
    public void Compile_BreakInNestedLoop_OnlyBreaksInnerLoop()
    {
        var script = """
            for "i" in {outer}
              for "j" in {inner}
                break
              say "after inner"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // 内层 break 的 JumpCommand + 外层 say 都应存在
        result.Commands.Should().Contain(c => c is JumpCommand);
        result.Commands.Should().Contain(c => c is ShowDialogCommand);
    }

    [Fact]
    public void Compile_BreakInWhile_FollowedByCode_AllJumpsResolved()
    {
        var script = """
            while {hp > 0}
              break
              say "never"
            say "after"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // 所有 JumpCommand 的 TargetIndex 必须已解析（>= 0）
        var allJumps = result.Commands.OfType<JumpCommand>().ToList();
        allJumps.Should().NotBeEmpty();
        foreach (var jmp in allJumps)
            jmp.TargetIndex.Should().BeGreaterThanOrEqualTo(0,
                "break 的 TargetIndex 必须被解析");
        // break 跳转目标应指向 "after" 之前（循环之后）
        var breakJump = allJumps.FirstOrDefault(j => j.TargetIndex > 0);
        breakJump.Should().NotBeNull();
    }

    [Fact]
    public void Compile_ContinueInFor_FollowedByCode_AllJumpsResolved()
    {
        var script = """
            for "i" in {items}
              continue
              say "never"
            say "after"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        var allJumps = result.Commands.OfType<JumpCommand>().ToList();
        allJumps.Should().NotBeEmpty();
        foreach (var jmp in allJumps)
            jmp.TargetIndex.Should().BeGreaterThanOrEqualTo(0,
                "continue 的 TargetIndex 必须被解析");
    }

    [Fact]
    public void Compile_WhileWithLabel_NoSpuriousEndCommand()
    {
        var script = """
            while {hp > 0}
              say "alive"
            say "done"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // 无用户 label 时不应有任何 EndCommand
        result.Commands.Should().NotContain(c => c is EndCommand,
            "循环后跟代码但无用户 label 时，不应有 EndCommand 哨兵阻断执行流");
    }

    [Fact]
    public void Compile_ForWithLabel_NoSpuriousEndCommand()
    {
        var script = """
            for "i" in {items}
              say "{i}"
            say "done"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        result.Commands.Should().NotContain(c => c is EndCommand,
            "for 循环后跟代码但无用户 label 时，不应有 EndCommand 哨兵阻断执行流");
    }

    [Fact]
    public void Compile_NestedWhileFollowedByCode_NoSpuriousEndCommand()
    {
        var script = """
            while {a > 0}
              while {b > 0}
                say "inner"
              say "after inner"
            say "after outer"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // 嵌套 while 无用户 label 时不应有 EndCommand
        result.Commands.Should().NotContain(c => c is EndCommand);
        // 应有 2 个 BranchCommand（内外 while 各一个）
        result.Commands.OfType<BranchCommand>().Should().HaveCount(2);
        // 应有 2 个 JumpCommand（内外 while 各一个回跳）
        result.Commands.OfType<JumpCommand>().Should().HaveCount(2);
    }

    // ========== 诊断测试：循环后代码可达性 ==========

    [Fact]
    public void Diagnostic_WhileFollowedByCode_BranchFalseTargetsAfterLoop()
    {
        var script = """
            while {hp > 0}
              say "alive"
            say "done"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();

        // 找到 "done" 命令的索引
        int doneIdx = -1;
        for (int i = 0; i < result.Commands.Count; i++)
        {
            if (result.Commands[i] is ShowDialogCommand sdc && sdc.Text == "done")
            {
                doneIdx = i;
                break;
            }
        }
        doneIdx.Should().BeGreaterThanOrEqualTo(0, "应该能找到 'done' 命令");

        // BranchCommand 在索引 0，false 目标 = 0 + SkipCount + 1
        var branch = result.Commands.OfType<BranchCommand>().First();
        int branchIdx = result.Commands.ToList().FindIndex(c => c is BranchCommand);
        int falseTarget = branchIdx + branch.SkipCount + 1;

        // 诊断输出到文件
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== While Followed By Code ===");
        sb.AppendLine($"BranchCommand at index {branchIdx}, SkipCount={branch.SkipCount}");
        sb.AppendLine($"False target = {falseTarget}, 'done' at index {doneIdx}");
        sb.AppendLine($"Command at false target: {result.Commands[falseTarget].GetType().Name}");
        for (int i = 0; i < result.Commands.Count; i++)
            sb.AppendLine($"  [{i}] {result.Commands[i].GetType().Name}");
        System.IO.File.WriteAllText("e:/Project/Engine/diag_while.txt", sb.ToString());

        falseTarget.Should().Be(doneIdx,
            "while 条件为 false 时应跳到循环后的代码 'done'，而不是 EndCommand");
    }

    [Fact]
    public void Diagnostic_ForFollowedByCode_BranchFalseTargetsAfterLoop()
    {
        var script = """
            for "i" in {items}
              say "{i}"
            say "done"
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();

        // 找到 "done" 命令的索引
        int doneIdx = -1;
        for (int i = 0; i < result.Commands.Count; i++)
        {
            if (result.Commands[i] is ShowDialogCommand sdc && sdc.Text == "done")
            {
                doneIdx = i;
                break;
            }
        }
        doneIdx.Should().BeGreaterThanOrEqualTo(0, "应该能找到 'done' 命令");

        // BranchCommand false 目标 = branchIdx + SkipCount + 1
        var branch = result.Commands.OfType<BranchCommand>().First();
        int branchIdx = result.Commands.ToList().FindIndex(c => c is BranchCommand);
        int falseTarget = branchIdx + branch.SkipCount + 1;

        // 诊断输出到文件
        var sb2 = new System.Text.StringBuilder();
        sb2.AppendLine($"=== For Followed By Code ===");
        sb2.AppendLine($"BranchCommand at index {branchIdx}, SkipCount={branch.SkipCount}");
        sb2.AppendLine($"False target = {falseTarget}, 'done' at index {doneIdx}");
        sb2.AppendLine($"Command at false target: {result.Commands[falseTarget].GetType().Name}");
        for (int i = 0; i < result.Commands.Count; i++)
            sb2.AppendLine($"  [{i}] {result.Commands[i].GetType().Name}");
        System.IO.File.WriteAllText("e:/Project/Engine/diag_for.txt", sb2.ToString());

        // for 循环 false 目标 = 清理命令（SetVariable(idx=null)），然后 fall through 到 "done"
        // 验证：false target 到 done 之间没有 EndCommand 阻断
        falseTarget.Should().BeLessThan(doneIdx,
            "for 条件为 false 时应跳到清理命令（在 'done' 之前）");
        result.Commands.Should().NotContain(c => c is EndCommand,
            "无用户 label 时不应有 EndCommand 哨兵");
        // 验证 false target 到 done 之间只有 SetVariableCommand（清理）
        for (int i = falseTarget; i < doneIdx; i++)
            result.Commands[i].Should().BeOfType<SetVariableCommand>(
                $"false target 到 'done' 之间应只有清理命令，但 [{i}] 是 {result.Commands[i].GetType().Name}");
    }

    // ========== Phase 24: viewport 元素 ==========

    [Fact]
    public void Compile_Viewport_ElementInScene()
    {
        var script = """
            scene "test_viewport"
              viewport x=100 y=100 width=400 height=300 scroll_h=true scroll_v=true
            """;
        var result = _engine.Compile(script);
        result.Success.Should().BeTrue();
        // scene 块编译为 SceneCommand，元素在运行时从场景实体中加载
        result.Commands.Should().Contain(c => c is SceneCommand);
        var sceneCmd = result.Commands.OfType<SceneCommand>().First();
        sceneCmd.SceneName.Should().Be("test_viewport");
    }
}
