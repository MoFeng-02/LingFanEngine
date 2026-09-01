using FluentAssertions;
using LingFanEngine.Dsl.LanguageService;
using Xunit;

namespace LingFanEngine.Tests.DslCore;

/// <summary>
/// 诊断测试：验证补全系统的核心逻辑是否正确。
/// 对应用户反馈的三个问题：
/// 1. 行首输入 b 时 bottom 不应出现（bottom 是属性名，非语句/元素关键字）
/// 2. button 后空格应触发子属性补全（text=, color=, nav= 等）
/// 3. 输入 { 应触发行内标记补全（{b}, {color=} 等）
/// </summary>
public class DslCompletionDiagnosticsTests
{
    private static DslLanguageService CreateService(string storyText, string filePath = "test.story")
    {
        var svc = new DslLanguageService();
        svc.UpdateDocument(filePath, storyText);
        return svc;
    }

    /// <summary>
    /// 问题 1：行首输入 b，StatementStart 上下文应只包含 Statements 和 UiElementTypes，
    /// 不应包含 bottom（bottom 是 ElementAttributes 中的属性名）。
    /// </summary>
    [Fact]
    public void StatementStart_Typing_B_ShouldNotInclude_Bottom()
    {
        var story = @"scene test
text ""hello""
";
        var svc = CreateService(story);
        // 光标在第 3 行行首（空行），输入 b
        var lineStart = story.IndexOf('\n', story.IndexOf('\n') + 1) + 1; // 第 3 行行首
        var offset = lineStart + 1; // 输入了 b（光标在 b 之后）
        var items = svc.GetCompletion("test.story", offset);

        var labels = items.Select(i => i.DisplayText).ToList();
        // bottom 不应出现在行首补全中（它是属性名，不是语句/元素）
        labels.Should().NotContain("bottom",
            "bottom 是 ElementAttributes 属性名，不应出现在 StatementStart 补全中");
        // button 应出现（它是 UI 元素类型）
        labels.Should().Contain("button",
            "button 是 UI 元素类型，应在 StatementStart 补全中");
    }

    /// <summary>
    /// 问题 2：button 后空格，应触发 ParameterName 上下文，
    /// 返回 button 的 NamedParams（text, color, nav, class, width, height 等）。
    /// </summary>
    [Fact]
    public void AfterButtonSpace_ShouldTriggerParameterNameCompletion()
    {
        var story = @"scene test
button 
text ""hello""
";
        var svc = CreateService(story);
        // 光标在第 2 行 "button " 的空格之后
        var line2Start = story.IndexOf('\n') + 1;
        var offset = line2Start + "button ".Length; // 光标在 button + 空格之后
        var items = svc.GetCompletion("test.story", offset);

        var labels = items.Select(i => i.DisplayText).ToList();
        // 应包含 button 的属性名
        labels.Should().Contain("text", "button 应有 text 属性补全");
        labels.Should().Contain("color", "button 应有 color 属性补全");
        labels.Should().Contain("nav", "button 应有 nav 属性补全");
        labels.Should().Contain("width", "button 应有 width 属性补全");
        labels.Should().Contain("height", "button 应有 height 属性补全");
        labels.Should().Contain("class", "button 应有 class 属性补全");
        // 不应包含 button 自身（它是元素名，不是属性名）
        labels.Should().NotContain("button",
            "button 是元素名，不应出现在其自身属性补全中");
    }

    /// <summary>
    /// 问题 3：在 { 后应触发 VariableReference 上下文，
    /// 包含行内标记（{b}, {color=} 等）。
    /// </summary>
    [Fact]
    public void InsideInterpolation_ShouldIncludeInlineTags()
    {
        var story = @"scene test
say ""text {";
        var svc = CreateService(story);
        // 光标在 { 之后（插值上下文）
        var sayLineStart = story.IndexOf('\n') + 1;
        var offset = sayLineStart + "say \"text {".Length;
        var items = svc.GetCompletion("test.story", offset);

        var labels = items.Select(i => i.DisplayText).ToList();
        // 行内标记不带花括号前缀（用户已输入 {，直接补全标记名）
        labels.Should().Contain("b", "插值上下文应包含 b 行内标记");
        labels.Should().Contain("color=", "插值上下文应包含 color= 行内标记");
        labels.Should().Contain("i", "插值上下文应包含 i 行内标记");
    }

    /// <summary>
    /// 在 scene 块内，text 元素后空格应触发属性补全。
    /// </summary>
    [Fact]
    public void InSceneBlock_TextElement_ShouldShowAttributes()
    {
        var story = @"scene test
text 
";
        var svc = CreateService(story);
        var line2Start = story.IndexOf('\n') + 1;
        var offset = line2Start + "text ".Length;
        var items = svc.GetCompletion("test.story", offset);

        var labels = items.Select(i => i.DisplayText).ToList();
        labels.Should().Contain("width", "text 元素应有 width 属性补全");
        labels.Should().Contain("height", "text 元素应有 height 属性补全");
        labels.Should().Contain("fontSize", "text 元素应有 fontSize 属性补全");
    }

    /// <summary>
    /// 验证 button 已有 text=100 时，补全不应再包含 text。
    /// </summary>
    [Fact]
    public void Button_WithUsedParam_ShouldExcludeFromCompletion()
    {
        var story = @"scene test
button text=""hello"" 
";
        var svc = CreateService(story);
        var line2Start = story.IndexOf('\n') + 1;
        var offset = line2Start + "button text=\"hello\" ".Length;
        var items = svc.GetCompletion("test.story", offset);

        var labels = items.Select(i => i.DisplayText).ToList();
        labels.Should().NotContain("text",
            "text 已使用，不应再出现在补全候选中");
        labels.Should().Contain("color",
            "color 未使用，应出现在补全候选中");
    }

    /// <summary>
    /// 验证 scene 块内行首输入 t，应同时包含 text 元素和 text 语句（text_typewriter）。
    /// </summary>
    [Fact]
    public void InSceneBlock_StatementStart_ShouldIncludeBothElementsAndStatements()
    {
        var story = @"scene test
";
        var svc = CreateService(story);
        var line2Start = story.IndexOf('\n') + 1;
        var offset = line2Start + 1; // 输入 t
        var items = svc.GetCompletion("test.story", offset);

        var labels = items.Select(i => i.DisplayText).ToList();
        labels.Should().Contain("text", "scene 块内应有 text UI 元素");
        // scene 块内常见语句
        labels.Should().Contain("show", "scene 块内应有 show 语句");
        labels.Should().Contain("animate", "scene 块内应有 animate 语句");
        // bottom 不应出现（它是属性名，不是语句/元素关键字）
        labels.Should().NotContain("bottom", "bottom 是属性名，不应出现在行首补全中");
    }
}
