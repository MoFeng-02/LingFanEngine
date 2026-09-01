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

        // 2026-09 契约：{fast} 越过快进点后剩余文本立即显示；返回干净文本（控制标签已剥离）
        result.Should().Be("headtail");
        engine.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Advance_WaitTag_PausesAtTag()
    {
        var engine = Create(out _);
        engine.SetText("ab{w}cd");

        var result = engine.Advance(10.0); // 一次推到标签位置

        result.Should().Be("ab"); // {w} 前停住，返回干净文本
        engine.IsPausedByTag.Should().BeTrue();
        engine.FullText.Should().Be("ab{w}cd"); // 原文保持不变（不再改写 _fullText）

        engine.ResumeFromPause();
        engine.IsPausedByTag.Should().BeFalse();
        var after = engine.Advance(10.0);
        after.Should().Be("abcd");
        engine.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Advance_WaitTagWithSeconds_AutoResumes()
    {
        var engine = Create(out _);
        engine.SetText("ab{w=0.1}cd");

        engine.Advance(10.0).Should().Be("ab"); // 停住
        engine.IsPausedByTag.Should().BeTrue();

        engine.Advance(0.05).Should().BeNull(); // 未到 0.1s 不恢复
        var after = engine.Advance(0.1); // 到时自动恢复并继续
        after.Should().Be("abcd");
        engine.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Advance_SurrogatePair_NeverSplits()
    {
        // emoji 是代理对——打字机任何位置都不能切出孤立半截代理
        var engine = Create(out _);
        var emoji = "\U0001F600\U0001F601\U0001F602"; // 3 个 emoji，6 个 UTF-16 单元
        engine.SetText(emoji);

        for (int i = 0; i < 20 && !engine.IsComplete; i++)
        {
            var partial = engine.Advance(0.02);
            if (partial != null)
            {
                // 任何前缀都不能以孤立高代理结尾
                (partial.Length == 0 || !char.IsHighSurrogate(partial[^1]))
                    .Should().BeTrue($"前缀 [{partial}] 不得以孤立高代理结尾（代理对不可切割）");
            }
        }
        engine.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Advance_PunctuationPause_SlowsReveal()
    {
        // 标点后停顿：有标点文本的推进应比无标点文本慢（同速同时长下显示更少）
        var plain = Create(out _);
        plain.SetText("abcdefghij");
        var withPunct = Create(out _);
        withPunct.SetText("ab，cdefghij");

        string? p = null, w = null;
        for (int i = 0; i < 3; i++)
        {
            p = plain.Advance(0.02);
            w = withPunct.Advance(0.02);
        }
        (w?.Length ?? 0).Should().BeLessThanOrEqualTo(p?.Length ?? 0,
            "标点停顿后，显示进度不应超过无标点对照");
    }

    [Fact]
    public void Advance_SpeedZero_DisplaysInstantly()
    {
        var engine = Create(out var state);
        state.Set(StateKeys.Dialog.TypewriterSpeed, 0.0); // 0 = 立即显示
        engine.SetText("立即显示");

        engine.Advance(0.001).Should().Be("立即显示");
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

    // ========== 溢出裁剪提示 + 分段渲染（C4/B1），2026-09 ==========

    [Fact]
    public void SetNvlText_RawSkipHint_ConvertsToVisibleCoords()
    {
        var engine = Create(out _);
        // 前段含样式标签（12 标签字符）：raw 坐标 ≠ 可见坐标
        engine.SetNvlText("{b}旧行{/b}\n");

        // 裁剪提示：保留部分原文长 10（{b}旧行{/b}\n = 3+2+4+1，标签字符不计可见）
        var skip = engine.SetNvlText("{b}旧行{/b}\n新内容", 10);

        skip.Should().Be(3, "SkipHint 按可见坐标换算：旧行 2 字 + 换行 = 3 个可见字符（标签不计宽）");
        engine.CharIndex.Should().Be(3);
        // 可见文本 = 「旧行\n新内容」；当前显示前 3 字符 = 旧行+换行
        engine.VisibleText().Should().Be("旧行\n");
    }

    [Fact]
    public void Segments_SplitByStyleTags_MergedStyles()
    {
        var engine = Create(out _);
        engine.SetText("普通{b}加粗{/b}尾部");

        var segs = engine.Segments;
        string.Concat(segs.Select(s => s.Text)).Should().Be("普通加粗尾部",
            "分段文本拼接 = 纯可见文本（标签剥离）");
        segs.Should().Contain(s => s.Text == "加粗" && s.Style.Bold, "加粗段样式正确");
        segs.Should().Contain(s => s.Text == "尾部" && !s.Style.Bold, "闭合标签后样式恢复");
    }

    [Fact]
    public void Advance_TypewriterFalseState_DisplaysInstantly()
    {
        var engine = Create(out var state);
        state.Set(StateKeys.Dialog.TypewriterEnabled, false);
        engine.SetText("不打字");

        engine.Advance(0.001).Should().Be("不打字");
        engine.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void TryConsumeClick_OncePerText()
    {
        var engine = Create(out _);
        engine.SetText("x");

        engine.TryConsumeClick().Should().BeTrue("本句首次点击应被消费");
        engine.TryConsumeClick().Should().BeFalse("重复点击（双击/快速点击）应被拒绝");
        engine.SetText("y");
        engine.TryConsumeClick().Should().BeTrue("新文本加载后点击重新生效");
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
