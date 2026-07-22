using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// DialogEngine 纯逻辑契约测试（F5/F11 补强）。
/// <para>DialogEngine 是从 DialogBox 提取的纯打字机/NVL/内联标记引擎（非 UserControl），
/// 除 ApplyInlineMarkup 填充 InlineCollection 外均为纯字符串/状态逻辑，可脱离 headless 验证：
/// SetText 重置、打字机逐帧推进、instant/关闭打字机跳末尾、{w}/{p}/{fast} 标签、
/// SkipToEnd、NVL 追加检测与 skipLength、末尾未闭合标签裁剪、内联标记解析。</para>
/// </summary>
public class DialogEngineTests
{
    private static DialogEngine Create(out StateContainer state)
    {
        state = new StateContainer();
        return new DialogEngine(state);
    }

    // ========== SetText / 基础状态 ==========

    [Fact]
    public void SetText_ResetsTypewriterState()
    {
        var engine = Create(out var state);

        engine.SetText("你好世界");

        engine.FullText.Should().Be("你好世界");
        engine.CharIndex.Should().Be(0);
        engine.IsComplete.Should().BeFalse();
        engine.IsPausedByTag.Should().BeFalse();
        state.Get<bool>(StateKeys.Dialog.TypewriterDone).Should().BeFalse();
    }

    [Fact]
    public void SetText_Empty_IsCompleteImmediately()
    {
        var engine = Create(out _);
        engine.SetText("");
        engine.IsComplete.Should().BeTrue();
        engine.Advance(0.1).Should().BeNull(); // IsComplete → null
    }

    // ========== 打字机逐帧推进 ==========

    [Fact]
    public void Advance_GraduallyRevealsPrefix()
    {
        var engine = Create(out _);
        engine.SetText("abcdefghijklmnop");

        var partial = engine.Advance(0.05); // 60 字符/秒 * 0.05s ≈ 3 字符起步

        partial.Should().NotBeNull();
        partial!.Length.Should().BeGreaterThan(0);
        partial.Length.Should().BeLessThan("abcdefghijklmnop".Length);
        "abcdefghijklmnop".Should().StartWith(partial);
        engine.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Advance_EnoughTime_CompletesAndSetsDone()
    {
        var engine = Create(out var state);
        engine.SetText("abcd");

        var result = engine.Advance(10.0); // 远超所需时间

        result.Should().Be("abcd");
        engine.IsComplete.Should().BeTrue();
        state.Get<bool>(StateKeys.Dialog.TypewriterDone).Should().BeTrue();
    }

    [Fact]
    public void Advance_RespectsTypewriterSpeedFromState()
    {
        var engine = Create(out var state);
        state.Set(StateKeys.Dialog.TypewriterSpeed, 1000.0); // 极快
        engine.SetText("abcdefghij");

        var result = engine.Advance(0.05); // 1000*0.05=50 > 10 → 全显示

        result.Should().Be("abcdefghij");
        engine.IsComplete.Should().BeTrue();
    }

    // ========== instant / 打字机开关 ==========

    [Fact]
    public void Advance_Instant_SkipsToEnd()
    {
        var engine = Create(out var state);
        state.Set(StateKeys.Dialog.Instant, true);
        engine.SetText("完整文本立即显示");

        var result = engine.Advance(0.001);

        result.Should().Be("完整文本立即显示");
        engine.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Advance_TypewriterDisabled_SkipsToEnd()
    {
        var engine = Create(out var state);
        state.Set(StateKeys.Dialog.TypewriterEnabled, false);
        engine.SetText("no typewriter");

        var result = engine.Advance(0.001);

        result.Should().Be("no typewriter");
        engine.IsComplete.Should().BeTrue();
    }

    // ========== {fast} / {w} / {p} 标签 ==========

    [Fact]
    public void Advance_FastTag_SkipsToEnd()
    {
        var engine = Create(out _);
        engine.SetText("head{fast}tail");

        var result = engine.Advance(0.001);

        // {fast} 触发 SkipToEnd，返回完整原文（标签剥离由 ApplyInlineMarkup 负责）
        result.Should().Be("head{fast}tail");
        engine.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Advance_WaitTag_PausesAndRemovesTag()
    {
        var engine = Create(out _);
        engine.SetText("ab{w}cd");

        var result = engine.Advance(10.0); // 一次推到标签位置

        result.Should().Be("ab");
        engine.IsPausedByTag.Should().BeTrue();
        engine.FullText.Should().Be("abcd"); // {w} 已从文本移除

        engine.ResumeFromPause();
        engine.IsPausedByTag.Should().BeFalse();
        var after = engine.Advance(10.0);
        after.Should().Be("abcd");
        engine.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Advance_WhilePaused_ReturnsNull()
    {
        var engine = Create(out _);
        engine.SetText("ab{w}cd");
        engine.Advance(10.0); // 进入暂停

        engine.Advance(10.0).Should().BeNull(); // 暂停中不推进
    }

    // ========== SkipToEnd ==========

    [Fact]
    public void SkipToEnd_ReturnsFullTextAndCompletes()
    {
        var engine = Create(out var state);
        engine.SetText("跳到末尾");

        var result = engine.SkipToEnd();

        result.Should().Be("跳到末尾");
        engine.IsComplete.Should().BeTrue();
        engine.IsPausedByTag.Should().BeFalse();
        state.Get<bool>(StateKeys.Dialog.TypewriterDone).Should().BeTrue();
    }

    // ========== NVL 追加检测 ==========

    [Fact]
    public void SetNvlText_FirstCall_ReturnsZeroSkip()
    {
        var engine = Create(out _);
        engine.SetNvlText("第一行").Should().Be(0);
        engine.NvlSkipLength.Should().Be(0);
    }

    [Fact]
    public void SetNvlText_Appended_SkipsExistingPortion()
    {
        var engine = Create(out _);
        engine.SetNvlText("第一行");

        var skip = engine.SetNvlText("第一行第二行"); // 追加

        skip.Should().Be("第一行".Length);
        engine.NvlSkipLength.Should().Be("第一行".Length);
        engine.CharIndex.Should().Be("第一行".Length); // 从追加处开始打字机
    }

    [Fact]
    public void SetNvlText_Replaced_ResetsSkip()
    {
        var engine = Create(out _);
        engine.SetNvlText("旧文本");

        var skip = engine.SetNvlText("全新的内容"); // 非追加

        skip.Should().Be(0);
        engine.NvlSkipLength.Should().Be(0);
    }

    [Fact]
    public void ResetNvlState_ClearsPrevText()
    {
        var engine = Create(out _);
        engine.SetNvlText("第一行");
        engine.ResetNvlState();

        // 重置后即便文本以旧内容开头，也视为全新开始
        engine.SetNvlText("第一行第二行").Should().Be(0);
    }

    // ========== StripTrailingUnclosedTag（静态纯函数） ==========

    [Fact]
    public void StripTrailingUnclosedTag_RemovesDanglingOpen()
    {
        DialogEngine.StripTrailingUnclosedTag("你好{col").Should().Be("你好");
    }

    [Fact]
    public void StripTrailingUnclosedTag_KeepsClosedTag()
    {
        DialogEngine.StripTrailingUnclosedTag("你好{color=#fff}")
            .Should().Be("你好{color=#fff}");
    }

    [Fact]
    public void StripTrailingUnclosedTag_NoBrace_ReturnsAsIs()
    {
        DialogEngine.StripTrailingUnclosedTag("纯文本").Should().Be("纯文本");
    }

    // ========== ApplyInlineMarkup（静态，填充 InlineCollection） ==========

    [Fact]
    public void ApplyInlineMarkup_ParsesBoldAndPlainRuns()
    {
        var inlines = new TextBlock().Inlines!;

        DialogEngine.ApplyInlineMarkup(inlines, "{b}加粗{/b}普通");

        inlines.Count.Should().Be(2);
        var bold = inlines[0].Should().BeOfType<Run>().Subject;
        bold.Text.Should().Be("加粗");
        bold.FontWeight.Should().Be(FontWeight.Bold);
        var plain = inlines[1].Should().BeOfType<Run>().Subject;
        plain.Text.Should().Be("普通");
        plain.FontWeight.Should().Be(FontWeight.Normal);
    }

    [Fact]
    public void ApplyInlineMarkup_StripsPauseTags()
    {
        var inlines = new TextBlock().Inlines!;

        DialogEngine.ApplyInlineMarkup(inlines, "a{w}b{p}c{fast}d");

        var text = string.Concat(inlines.OfType<Run>().Select(r => r.Text));
        text.Should().Be("abcd");
    }

    [Fact]
    public void ApplyInlineMarkup_AppliesColor()
    {
        var inlines = new TextBlock().Inlines!;

        DialogEngine.ApplyInlineMarkup(inlines, "{color=#FF0000}红字{/color}");

        var run = inlines.OfType<Run>().Single();
        run.Text.Should().Be("红字");
        run.Foreground.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(Color.Parse("#FF0000"));
    }
}
