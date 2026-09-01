using FluentAssertions;
using LingFanEngine.DslCore;
using Xunit;

namespace LingFanEngine.Tests.DslCore;

/// <summary>
/// Parlot 语义陷阱探针（2026-09 迁移审计）：
/// 用实测行为钉死 Parlot 1.5.8 与 Pidgin 的底层语义差异——
/// 大小写敏感性、关键字前缀冲突、Eof 包装、Optional/Or 行为。
/// 任何一条失败都意味着「静默语义漂移」，必须修 DslStatementParser 而不是改测试。
/// </summary>
public class ParlotSemanticProbeTests
{
    // ====== 大小写敏感性：Pidgin String() 区分大小写，"SAY" 必须解析失败 ======

    [Theory]
    [InlineData("SAY \"hi\"", typeof(SayStmt))]
    [InlineData("WHILE {x}", typeof(WhileStmt))]
    [InlineData("FOR \"i\" in {items}", typeof(ForStmt))]
    [InlineData("While {x}", typeof(WhileStmt))]
    public void Keywords_AreCaseSensitive_WrongCaseMissesStatement(string line, System.Type missed)
    {
        // 大小写不匹配时或返回 null、或兜底为 UI 元素行——均不得命中目标语句类型
        var stmt = DslStatementParser.ParseLine(line);
        Assert.False(stmt is not null && stmt.GetType() == missed,
            $"Pidgin String() 区分大小写，[{line}] 不得命中 {missed.Name}（Parlot Text 若默认不区分大小写则属语义漂移）");
    }

    // ====== 关键字前缀冲突：Eof 包装必须拦截截断匹配 ======

    [Theory]
    [InlineData("skip_time 30", typeof(SkipTimeStmt))]
    [InlineData("skip", typeof(ToggleSkipStmt))]
    [InlineData("auto_save true", typeof(AutoSaveStmt))]
    [InlineData("auto", typeof(ToggleAutoStmt))]
    [InlineData("auto_speed 2.0", typeof(AutoSpeedStmt))]
    [InlineData("set_time_event \"id\" 12", typeof(SetTimeEventStmt))]
    [InlineData("set \"k\" 5", typeof(SetStmt))]
    [InlineData("save_delete \"slot\"", typeof(SaveDeleteStmt))]
    [InlineData("gallery_unlock \"id\"", typeof(GalleryUnlockStmt))]
    [InlineData("pause_video", typeof(PauseVideoStmt))]
    [InlineData("pause 2", typeof(PauseStmt))]
    [InlineData("stop_ambient", typeof(StopAmbientStmt))]
    [InlineData("ambient \"p\" loop=true", typeof(AmbientStmt))]
    public void KeywordPrefixCollision_LongKeywordWins(string line, System.Type expected)
    {
        var stmt = DslStatementParser.ParseLine(line);
        stmt.Should().NotBeNull($"[{line}] 应解析成功");
        stmt.Should().BeOfType(expected, $"[{line}] 应命中长关键字而非被短关键字截断");
    }

    // ====== 循环/块结构核心：for/while/if/foreach/switch ======

    [Fact]
    public void WhileStatement_ParsesCondition()
    {
        var stmt = DslStatementParser.ParseLine("while {i < 5}");
        var w = stmt.Should().BeOfType<WhileStmt>().Subject;
        w.Condition.Should().Be("i < 5");
    }

    [Fact]
    public void IfStatement_ParsesCondition()
    {
        var stmt = DslStatementParser.ParseLine("if {gold >= 10}");
        var i = stmt.Should().BeOfType<IfStmt>().Subject;
        i.Condition.Should().Be("gold >= 10");
    }

    [Fact]
    public void ElseIfStatement_ParsesCondition()
    {
        var stmt = DslStatementParser.ParseLine("else if {gold < 10}");
        var e = stmt.Should().BeOfType<ElseIfStmt>().Subject;
        e.Condition.Should().Be("gold < 10");
    }

    [Fact]
    public void SwitchStatement_ParsesExpression()
    {
        var stmt = DslStatementParser.ParseLine("switch {choice}");
        var s = stmt.Should().BeOfType<SwitchStmt>().Subject;
        s.Expression.Should().Be("choice");
    }

    [Fact]
    public void ForStatement_ParsesVarNameAndSourceExpr()
    {
        var stmt = DslStatementParser.ParseLine("for \"i\" in {items}");
        var f = stmt.Should().BeOfType<ForStmt>().Subject;
        f.VarName.Should().Be("i");
        f.SourceExpr.Should().Be("items");
    }

    [Fact]
    public void ForeachStatement_ParsesVarNameAndSourceKey()
    {
        var stmt = DslStatementParser.ParseLine("foreach \"v\" in \"myArray\"");
        var f = stmt.Should().BeOfType<ForeachStmt>().Subject;
        f.VarName.Should().Be("v");
        f.SourceKey.Should().Be("myArray");
    }

    // ====== 复合参数语句（此前 And/SkipAnd 混错高发区）======

    [Fact]
    public void ShowStatement_FullForm_Parses()
    {
        var stmt = DslStatementParser.ParseLine("show \"bg1\" at (100, 200) with \"fade\" duration=0.5");
        var s = stmt.Should().BeOfType<ShowStmt>().Subject;
        s.Target.Should().Be("bg1");
        s.X.Should().Be(100);
        s.Y.Should().Be(200);
        s.Transition.Should().Be("fade");
        s.TransitionDuration.Should().Be(0.5);
    }

    [Fact]
    public void SayStatement_SpeakerSyntax_Parses()
    {
        var stmt = DslStatementParser.ParseLine("say \"你好\" speaker=\"小明\" clickable=true noskip=true instant=true");
        var s = stmt.Should().BeOfType<SayStmt>().Subject;
        s.Text.Should().Be("你好");
        s.Speaker.Should().Be("小明");
        s.Clickable.Should().BeTrue();
        s.Noskip.Should().BeTrue();
        s.Instant.Should().BeTrue();
    }

    [Fact]
    public void SayStatement_BySyntax_Parses()
    {
        var stmt = DslStatementParser.ParseLine("say \"你好\" by \"小明\" okey");
        var s = stmt.Should().BeOfType<SayStmt>().Subject;
        s.Speaker.Should().Be("小明");
        s.Clickable.Should().BeTrue();
    }

    [Fact]
    public void SaveLoad_BothVariants_Parses()
    {
        DslStatementParser.ParseLine("save \"slot1\" title \"标题\" screenshot=false")
            .Should().BeOfType<SaveStmt>();
        DslStatementParser.ParseLine("load \"slot1\"")
            .Should().BeOfType<LoadStmt>();
    }

    [Fact]
    public void CharacterStatement_Properties_Parses()
    {
        var stmt = DslStatementParser.ParseLine("character \"hero\" name=\"主角\" color=\"#FF0000\"");
        var c = stmt.Should().BeOfType<CharacterStmt>().Subject;
        c.Key.Should().Be("hero");
        c.Properties["name"].Should().Be("主角");
        c.Properties["color"].Should().Be("#FF0000");
    }

    [Fact]
    public void InputStatement_Options_Parses()
    {
        var stmt = DslStatementParser.ParseLine("input \"你的名字？\" store \"player_name\" options=[\"是\",\"否\"]");
        var i = stmt.Should().BeOfType<InputStmt>().Subject;
        i.Prompt.Should().Be("你的名字？");
        i.StoreKey.Should().Be("player_name");
        i.Options.Should().BeEquivalentTo(["是", "否"]);
    }

    [Fact]
    public void NvlStatement_Variants_Parses()
    {
        DslStatementParser.ParseLine("nvl clear").Should().BeOfType<NvlStmt>();
        DslStatementParser.ParseLine("nvl").Should().BeOfType<NvlStmt>();
    }

    [Fact]
    public void SetStatement_Value_Parses()
    {
        var stmt = DslStatementParser.ParseLine("set \"gold\" 100");
        var s = stmt.Should().BeOfType<SetStmt>().Subject;
        s.Key.Should().Be("gold");
        s.ValuePart.Should().Be("100");
    }

    [Fact]
    public void DefineStatement_OnceSuffix_Parses()
    {
        var stmt = DslStatementParser.ParseLine("define \"pi\" 3.14 once");
        var d = stmt.Should().BeOfType<DefineStmt>().Subject;
        d.Key.Should().Be("pi");
        d.ValuePart.Should().Be("3.14");
    }

    [Fact]
    public void LabelStatement_Colon_Parses()
    {
        var stmt = DslStatementParser.ParseLine("label start:");
        var l = stmt.Should().BeOfType<LabelStmt>().Subject;
        l.Name.Should().Be("start");
    }

    [Fact]
    public void MenuOptionStatement_Parses()
    {
        var stmt = DslStatementParser.ParseLine("\"选项一\" -> target_label");
        var m = stmt.Should().BeOfType<MenuOptionStmt>().Subject;
        m.Text.Should().Be("选项一");
        m.TargetLabel.Should().Be("target_label");
    }

    [Fact]
    public void JumpAndCall_Parses()
    {
        DslStatementParser.ParseLine("jump start").Should().BeOfType<JumpStmt>();
        DslStatementParser.ParseLine("call helper").Should().BeOfType<CallStmt>();
    }

    // ====== 行内注释：引号外的 // 才是注释，引号内保留 ======

    [Fact]
    public void InlineComment_OutsideQuotes_Stripped()
    {
        var engine = new LingFanEngine.Services.Scripting.LingFanDslEngine();
        var result = engine.Compile("set \"x\" 5 // 注释\nsay \"你好 // 世界\"");
        result.Success.Should().BeTrue();
        var set = result.Commands.OfType<LingFanEngine.Services.Core.SetVariableCommand>().First();
        set.Key.Should().Be("x");
    }
}
