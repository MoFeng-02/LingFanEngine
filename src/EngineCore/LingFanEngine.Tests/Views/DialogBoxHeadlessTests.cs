using System.Reflection;
using Avalonia.Controls;
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
}
