using Avalonia.Controls;
using FluentAssertions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// E5（Views 渲染契约）Tier A 测试：直测 ControlFactory 是否正确消费 font 与 grid 附着属性。
/// <para>不渲染、不依赖 headless 宿主——Avalonia 控件的 FontFamily / Grid 附加属性在脱离 Application 上下文时
/// 即可实例化并读取，足以证明"font/grid 真的被消费且值正确"（E5 核心质疑）。</para>
/// <para>覆盖：font 在 text/button/ApplyCommonProps 三处路径；font 为空不覆盖默认（B5）；grid col/row/colspan/rowspan
/// 经 Grid.SetXxx 正确写入子控件；grid columns/rows 解析；无附着属性时保持默认 0。</para>
/// </summary>
public class ControlFactoryTests
{
    private sealed class FakeI18n : II18nService
    {
        public string Translate(string original) => original;
        public void SwitchLanguage(string lang) { }
        public System.Collections.Generic.IReadOnlyList<string> GetAvailableLanguages()
            => new[] { "zh-CN" };
    }

    private static ControlFactory CreateFactory()
        => new(new FakeI18n(), new StateContainer());

    private static UIElementEntity Entity(string type,
        System.Collections.Generic.Dictionary<string, object>? props = null,
        System.Collections.Generic.List<UIElementEntity>? children = null)
        => new() { ElementType = type, Properties = props ?? new(), Children = children ?? new() };

    // ========== font 消费 ==========

    [Fact]
    public void Font_Text_SetsFontFamily()
    {
        var factory = CreateFactory();
        var e = Entity("text", new() { ["text"] = "hi", ["font"] = "Microsoft YaHei" });

        var ctrl = factory.ConvertToControl(e);

        ctrl.Should().BeOfType<TextBlock>();
        var tb = (TextBlock)ctrl!;
        tb.FontFamily.Should().NotBeNull();
        tb.FontFamily!.ToString().Should().Contain("YaHei");
    }

    [Fact]
    public void Font_Empty_LeavesDefaultNull()
    {
        // B5：font 为空/不设置时不覆盖 Avalonia 默认字体（Avalonia 默认 FontFamily 为 "$Default" 哨兵）
        var factory = CreateFactory();
        var e = Entity("text", new() { ["text"] = "hi" });

        var tb = (TextBlock)factory.ConvertToControl(e)!;

        tb.FontFamily.Should().NotBeNull();
        tb.FontFamily!.ToString().Should().Be("$Default");
    }

    [Fact]
    public void Font_Button_SetsFontFamilyOnContentText()
    {
        // 覆盖 ApplyCommonProps(:851-852) 的 button 路径（Content 为 TextBlock 时设置其 FontFamily）。
        // 注：button 的 ConvertToControl 路径会创建 Cursor（需 Avalonia 服务定位器/headless 宿主），
        // 此处直接对 Button+TextBlock 内容调用 ApplyLayout 验证 font 消费，保持 Tier A 不依赖 headless。
        var factory = CreateFactory();
        var btn = new Button { Content = new TextBlock() };

        factory.ApplyLayout(btn, new() { ["font"] = "Consolas" }, 1280, 720);

        btn.Content.Should().BeOfType<TextBlock>();
        var btnTb = (TextBlock)btn.Content!;
        btnTb.FontFamily.Should().NotBeNull();
        btnTb.FontFamily!.ToString().Should().Contain("Consolas");
    }

    [Fact]
    public void Font_Text_ApplyCommonProps_SetsFontFamily()
    {
        // 覆盖 ApplyCommonProps(:851) 的 text 路径（面板等父容器经 ApplyLayout 触达 TextBlock）
        var factory = CreateFactory();
        var tb = new TextBlock();

        factory.ApplyLayout(tb, new() { ["font"] = "Segoe UI" }, 1280, 720);

        tb.FontFamily.Should().NotBeNull();
        tb.FontFamily!.ToString().Should().Contain("Segoe UI");
    }

    // ========== grid 附着属性消费 ==========

    [Fact]
    public void Grid_ColumnRowSpan_AppliedToChild()
    {
        var factory = CreateFactory();
        var child = Entity("text", new()
        {
            ["text"] = "x",
            ["col"] = "1",
            ["row"] = "0",
            ["colspan"] = "1",
            ["rowspan"] = "2"
        });
        var gridEntity = Entity("grid",
            new() { ["columns"] = "*,*", ["rows"] = "auto,auto" },
            new() { child });

        var ctrl = factory.ConvertToControl(gridEntity);

        ctrl.Should().BeOfType<Grid>();
        var g = (Grid)ctrl!;
        g.ColumnDefinitions.Count.Should().Be(2);
        g.RowDefinitions.Count.Should().Be(2);
        g.Children.Should().ContainSingle();
        var childCtrl = g.Children[0];
        Grid.GetColumn(childCtrl).Should().Be(1);
        Grid.GetRow(childCtrl).Should().Be(0);
        Grid.GetColumnSpan(childCtrl).Should().Be(1);
        Grid.GetRowSpan(childCtrl).Should().Be(2);
    }

    [Fact]
    public void Grid_ChildWithoutAttachment_StaysDefault()
    {
        var factory = CreateFactory();
        var child = Entity("text", new() { ["text"] = "x" });
        var gridEntity = Entity("grid",
            new() { ["columns"] = "*,*", ["rows"] = "auto" },
            new() { child });

        var g = (Grid)factory.ConvertToControl(gridEntity)!;
        var childCtrl = g.Children[0];

        Grid.GetColumn(childCtrl).Should().Be(0);
        Grid.GetRow(childCtrl).Should().Be(0);
    }
}
