using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// B2：DialogBox headless 渲染契约（Tier B）。
/// <para>DialogBox 是 public UserControl，构造器仅 (IStateContainer)。其契约是"SetText 真的改写控件属性 /
/// 可见性 / 对齐"，不依赖真实位图或字体管理器，故直接实例化 + 属性断言即可（无需 headless 会话）。</para>
/// <para>覆盖：ADV 模式（底部条 + 说话者可见）、NVL 模式（全屏拉伸 + 隐藏说话者）、Hide()、缺图侧脸图不崩且隐藏。</para>
/// <para>规避：BackgroundImage / UpdateSideImage 真实位图加载走 new Bitmap(真实路径) 会被 try/catch 吞掉；本测试不传真实路径，
/// 只验证"缺图不抛 + 侧脸图区域隐藏"的契约。PointerPressed 三态依赖 PointerPressedEventArgs（需真实 Pointer，难以构造），
/// 其状态写入逻辑由 DialogEngine 单测 + B1 覆盖，此处不重复。</para>
/// </summary>
public class DialogBoxHeadlessTests
{
    private sealed class FakeI18n : II18nService
    {
        public string Translate(string original) => original;
        public void SwitchLanguage(string lang) { }
        public IReadOnlyList<string> GetAvailableLanguages() => new[] { "zh-CN" };
    }

    private static DialogBox Create(out StateContainer state)
    {
        state = new StateContainer();
        return new DialogBox(state);
    }

    private static T GetPrivate<T>(DialogBox box, string name) =>
        (T)typeof(DialogBox).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(box)!;

    [Fact]
    public void SetText_AdvMode_ShowsBottomDialog_WithSpeakerVisible()
    {
        var box = Create(out var state);
        box.SetText("你好，世界", "爱丽丝");

        box.IsComplete.Should().BeFalse(); // 打字机未结束
        var root = (Border)box.Content!;
        root.IsVisible.Should().BeTrue();
        root.VerticalAlignment.Should().Be(VerticalAlignment.Bottom);
        root.HorizontalAlignment.Should().Be(HorizontalAlignment.Stretch);

        // 说话者文本块（_speakerText）应可见且文本为说话者名
        var speaker = GetPrivate<TextBlock>(box, "_speakerText");
        speaker.IsVisible.Should().BeTrue();
        speaker.Text.Should().Be("爱丽丝");
    }

    [Fact]
    public void SetText_NvlMode_ShowsFullscreenDialog_HidesSpeaker()
    {
        var box = Create(out var state);
        state.Set(StateKeys.Nvl.Active, true);
        box.SetText("第一章 起始");

        var root = (Border)box.Content!;
        root.IsVisible.Should().BeTrue();
        root.VerticalAlignment.Should().Be(VerticalAlignment.Stretch);
        root.HorizontalAlignment.Should().Be(HorizontalAlignment.Stretch);

        // NVL 模式隐藏单个说话者
        var speaker = GetPrivate<TextBlock>(box, "_speakerText");
        speaker.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Hide_ClearsRootAndSideImageVisibility()
    {
        var box = Create(out var state);
        box.SetText("可见对话", "角色");
        var root = (Border)box.Content!;
        root.IsVisible.Should().BeTrue();

        box.Hide();
        root.IsVisible.Should().BeFalse();

        var side = GetPrivate<Image>(box, "_sideImage");
        side.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void UpdateSideImage_EmptyPath_HidesSideImage()
    {
        // 确定性分支（DialogBox.cs:197-202）：路径为空 → 直接隐藏侧脸图，不触发任何位图加载。
        var box = Create(out var state);
        state.Set(StateKeys.Dialog.SideImage, "");

        var act = () => box.SetText("无侧脸图");

        act.Should().NotThrow();
        var side = GetPrivate<Image>(box, "_sideImage");
        side.IsVisible.Should().BeFalse(); // 空路径 → 隐藏侧脸图区域
    }

    [Fact]
    public void UpdateSideImage_MissingPath_DoesNotThrow()
    {
        // 真实契约：缺失（不存在）的侧脸图路径不得令 SetText 抛异常。
        // 注意：是否"隐藏"取决于 new Bitmap(缺失路径) 是否抛异常（受 Avalonia imaging 全局状态影响，
        // headless 会话污染后可能不抛），故此处只锁"不崩溃"这一稳健契约，不锁 IsVisible。
        var box = Create(out var state);
        state.Set(StateKeys.Dialog.SideImage, "this_file_does_not_exist.png");

        var act = () => box.SetText("带侧脸图");

        act.Should().NotThrow();
    }

    [Fact]
    public void Advance_IncrementalRender_DoesNotDuplicateRuns()
    {
        // 回归（2026-09 文字叠加 BUG）：边界段曾每帧追加新 Run（前缀快照叠加成
        // "这|这就|这就是传|…"），原位更新分支是死代码。修复后 Run 数恒 ≤ 分段数。
        var box = Create(out var state);
        state.Set(StateKeys.Dialog.TypewriterSpeed, 10.0); // 10 可见字符/秒
        box.SetText("这就是传说中的{color=#FFD700}迷雾小镇{/color}");

        var content = GetPrivate<TextBlock>(box, "_contentText");
        var maxRuns = 0;
        for (var i = 0; i < 40 && !box.IsComplete; i++)
        {
            box.Advance(0.1); // 每步 1 字符
            maxRuns = Math.Max(maxRuns, content.Inlines!.Count);
        }

        box.IsComplete.Should().BeTrue();
        // 文本仅 2 个分段（样式切换点），Run 数不得随帧增长
        maxRuns.Should().BeLessThanOrEqualTo(2);
        string.Concat(content.Inlines!.OfType<Run>().Select(r => r.Text))
            .Should().Be("这就是传说中的迷雾小镇");
    }

    [Fact]
    public void SetText_NvlAppend_TypewriterResumesAtSkipPosition()
    {
        // 回归（2026-09 NVL 点击推进延迟）：NVL 追加检测失败会导致整段累积文本从零重打
        // （累积 2-4 行 ≈ 1 秒，用户感知为点击后新句延迟出现）。
        // 契约：第二句 SetText 后打字机应从旧行末尾继续（跳过已显示部分）。
        var box = Create(out var state);
        state.Set(StateKeys.Nvl.Active, true);
        state.Set(StateKeys.Nvl.SkipHint, -1);
        state.Set(StateKeys.Dialog.TypewriterSpeed, 60.0);

        var line1 = "{color=#88FF88}{b}诗人{/b}{/color}：迷雾笼罩着小镇。";
        var line2 = "{color=#88FF88}{b}诗人{/b}{/color}：旅人踏着暮色而来。";

        // 第一句：从零打完
        box.SetText(line1);
        while (!box.IsComplete) box.Advance(1.0 / 60);
        box.IsComplete.Should().BeTrue();

        // 第二句（累积追加，模拟 ShowDialogHandler 的 Nvl.Text）
        box.SetText(line1 + "\n" + line2);

        // 契约 1：未完成（还有新行要打），但旧行已完整显示（不重打）
        box.IsComplete.Should().BeFalse();
        var content = GetPrivate<TextBlock>(box, "_contentText");
        string.Concat(content.Inlines!.OfType<Run>().Select(r => r.Text))
            .Should().Be("诗人：迷雾笼罩着小镇。", "旧行应立即完整显示，不打字机重放");

        // 契约 2：第一帧即开始打新行（≤3 帧 = 50ms 内出现新内容）
        var advanced = false;
        for (var i = 0; i < 3 && !advanced; i++)
        {
            box.Advance(1.0 / 60);
            var shown = string.Concat(content.Inlines!.OfType<Run>().Select(r => r.Text));
            if (shown.Length > "诗人：迷雾笼罩着小镇。".Length) advanced = true;
        }
        advanced.Should().BeTrue("新行应在点击后的前几帧内立即开始显示，而非约 1 秒后");
    }

    [Fact]
    public void SetText_NvlOverflowSkipHint_TypewriterSkipsTrimmedPrefix()
    {
        // 回归（2026-09 NVL 溢出裁剪）：FullScreenNvlDialogBox 旧版不传递 SkipHint，
        // 裁剪后前缀变化导致追加检测失败 → 整段文本从零重打。
        // 契约：SkipHint（raw 坐标）传递后打字机从裁剪保留部分的末尾继续。
        var box = Create(out var state);
        state.Set(StateKeys.Nvl.Active, true);
        state.Set(StateKeys.Dialog.TypewriterSpeed, 60.0);

        var line1 = "{color=#88FF88}{b}诗人{/b}{/color}：第一行。";
        var line2 = "第二行，被滚出视口的行已裁剪。";
        var trimmed = line1 + "\n" + line2; // 模拟 TrimOldestLines 后的保留文本

        // 溢出裁剪：ShowDialogHandler 设 SkipHint = 裁剪后文本的 raw 长度
        state.Set(StateKeys.Nvl.SkipHint, trimmed.Length);
        box.SetText(trimmed + "\n第三行。");

        box.IsComplete.Should().BeFalse();
        var content = GetPrivate<TextBlock>(box, "_contentText");
        // 保留部分（可见文本）应立即完整显示——不重打
        string.Concat(content.Inlines!.OfType<Run>().Select(r => r.Text))
            .Should().Be("诗人：第一行。\n第二行，被滚出视口的行已裁剪。");
    }
}
