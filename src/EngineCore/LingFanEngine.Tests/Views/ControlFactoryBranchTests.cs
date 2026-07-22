using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAssertions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// B1-b：ControlFactory Tier A 盲区测试（无宿主）。
/// <para>覆盖 font/grid 之外的元素类型分支、ApplyLayout 定位数学、ApplyCommonProps 通用属性。
/// 刻意绕开需 Avalonia 服务定位器的 new Cursor(Hand)（button/choice 分支与 cursor 属性）
/// 与需原生后端的 video/GpuMediaPlayer 分支——这两者留待 B2/手动层。</para>
/// <para>图片分支用不存在的文件路径：LoadSource 非 avares 路径走 fire-and-forget 异步且 File.Exists=false 时提前返回，无副作用。</para>
/// </summary>
public class ControlFactoryBranchTests
{
    private sealed class FakeI18n : II18nService
    {
        public string Translate(string original) => original;
        public void SwitchLanguage(string lang) { }
        public IReadOnlyList<string> GetAvailableLanguages() => new[] { "zh-CN" };
    }

    private static ControlFactory CreateFactory(StateContainer? state = null)
        => new(new FakeI18n(), state ?? new StateContainer());

    private static UIElementEntity Entity(string type,
        Dictionary<string, object>? props = null,
        List<UIElementEntity>? children = null)
        => new() { ElementType = type, Properties = props ?? new(), Children = children ?? new() };

    // ========== ProgressBar (vbar / bar) ==========

    [Fact]
    public void Vbar_CreatesVerticalProgressBar_WithClampedValue()
    {
        var ctrl = CreateFactory().ConvertToControl(Entity("vbar",
            new() { ["value"] = "150", ["max"] = "100" }));

        ctrl.Should().BeOfType<ProgressBar>();
        var bar = (ProgressBar)ctrl!;
        bar.Orientation.Should().Be(Orientation.Vertical);
        bar.Maximum.Should().Be(100);
        bar.Value.Should().Be(100); // clamped
    }

    [Fact]
    public void Bar_CreatesHorizontalProgressBar()
    {
        var bar = (ProgressBar)CreateFactory().ConvertToControl(Entity("bar",
            new() { ["value"] = "30", ["max"] = "60" }))!;

        bar.Orientation.Should().Be(Orientation.Horizontal);
        bar.Maximum.Should().Be(60);
        bar.Value.Should().Be(30);
        bar.Height.Should().Be(20);
    }

    // ========== Image stretch ==========

    [Theory]
    [InlineData("fill", Stretch.Fill)]
    [InlineData("uniformtofill", Stretch.UniformToFill)]
    [InlineData("tofill", Stretch.UniformToFill)]
    [InlineData("uniform", Stretch.Uniform)]
    [InlineData("weird", Stretch.Uniform)]
    public void Image_StretchString_MapsCorrectly(string stretchStr, Stretch expected)
    {
        var img = (Image)CreateFactory().ConvertToControl(Entity("image",
            new() { ["source"] = "missing.png", ["stretch"] = stretchStr }))!;
        img.Stretch.Should().Be(expected);
    }

    [Fact]
    public void Image_FullPercentWidthHeight_UsesUniformToFill()
    {
        var img = (Image)CreateFactory().ConvertToControl(Entity("image",
            new() { ["source"] = "missing.png", ["width"] = "100%", ["height"] = "100%" }))!;
        img.Stretch.Should().Be(Stretch.UniformToFill);
    }

    [Fact]
    public void Image_EmptySource_ReturnsNull()
        => CreateFactory().ConvertToControl(Entity("image", new() { ["source"] = "" }))
            .Should().BeNull();

    [Fact]
    public void Background_CreatesUniformToFillImage()
    {
        var img = (Image)CreateFactory().ConvertToControl(Entity("background",
            new() { ["source"] = "bg.png" }))!;
        img.Stretch.Should().Be(Stretch.UniformToFill);
        img.HorizontalAlignment.Should().Be(HorizontalAlignment.Stretch);
    }

    // ========== 容器：panel / vbox / hbox / stack ==========

    [Fact]
    public void Vbox_ProducesBorderWithVerticalStack()
    {
        var border = (Border)CreateFactory().ConvertToControl(Entity("vbox",
            null, new() { Entity("text", new() { ["text"] = "a" }) }))!;
        border.Child.Should().BeOfType<StackPanel>();
        ((StackPanel)border.Child!).Orientation.Should().Be(Orientation.Vertical);
    }

    [Fact]
    public void Hbox_ProducesHorizontalStack()
    {
        var border = (Border)CreateFactory().ConvertToControl(Entity("hbox",
            null, new() { Entity("text", new() { ["text"] = "a" }) }))!;
        ((StackPanel)border.Child!).Orientation.Should().Be(Orientation.Horizontal);
    }

    [Fact]
    public void Panel_DirectionOverride_TakesPrecedence()
    {
        var border = (Border)CreateFactory().ConvertToControl(Entity("panel",
            new() { ["direction"] = "horizontal" },
            new() { Entity("text", new() { ["text"] = "a" }) }))!;
        ((StackPanel)border.Child!).Orientation.Should().Be(Orientation.Horizontal);
    }

    [Fact]
    public void Panel_Spacing_AppliedToInnerStack()
    {
        var border = (Border)CreateFactory().ConvertToControl(Entity("panel",
            new() { ["spacing"] = "12" },
            new() { Entity("text", new() { ["text"] = "a" }) }))!;
        ((StackPanel)border.Child!).Spacing.Should().Be(12);
    }

    [Fact]
    public void Stack_Direction_Horizontal()
    {
        var stack = (StackPanel)CreateFactory().ConvertToControl(Entity("stack",
            new() { ["direction"] = "horizontal" }))!;
        stack.Orientation.Should().Be(Orientation.Horizontal);
    }

    // ========== scroll / viewport ==========

    [Fact]
    public void Scroll_CreatesScrollViewerWithAutoBars()
    {
        var sv = (ScrollViewer)CreateFactory().ConvertToControl(Entity("scroll"))!;
        sv.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
        sv.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
    }

    [Fact]
    public void Viewport_ScrollFlags_Respected()
    {
        var sv = (ScrollViewer)CreateFactory().ConvertToControl(Entity("viewport",
            new() { ["scroll_h"] = "true", ["scroll_v"] = "false" }))!;
        sv.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
        sv.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
    }

    [Fact]
    public void Viewport_Defaults_HorizontalDisabled_VerticalAuto()
    {
        var sv = (ScrollViewer)CreateFactory().ConvertToControl(Entity("viewport"))!;
        sv.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
        sv.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
    }

    // ========== slider / checkbox ==========

    [Fact]
    public void Slider_MinMaxValueOrientation()
    {
        var slider = (Slider)CreateFactory().ConvertToControl(Entity("slider",
            new() { ["min"] = "0", ["max"] = "10", ["value"] = "5", ["orientation"] = "vertical" }))!;
        slider.Minimum.Should().Be(0);
        slider.Maximum.Should().Be(10);
        slider.Value.Should().Be(5);
        slider.Orientation.Should().Be(Orientation.Vertical);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Checkbox_IsCheckedFromString(string chk, bool expected)
    {
        var cb = (CheckBox)CreateFactory().ConvertToControl(Entity("checkbox",
            new() { ["text"] = "ok", ["checked"] = chk }))!;
        cb.IsChecked.Should().Be(expected);
        cb.Content.Should().Be("ok");
    }

    // ========== canvas / border / separator / spacer ==========

    [Fact]
    public void Canvas_ChildPositioned_ViaCanvasLeftTop()
    {
        var canvas = (Canvas)CreateFactory().ConvertToControl(Entity("canvas", null,
            new() { Entity("text", new() { ["text"] = "a", ["x"] = "100", ["y"] = "50" }) }))!;
        canvas.Children.Should().ContainSingle();
        var child = canvas.Children[0];
        Canvas.GetLeft(child).Should().Be(100);
        Canvas.GetTop(child).Should().Be(50);
    }

    [Fact]
    public void Border_WrapsChild()
    {
        var border = (Border)CreateFactory().ConvertToControl(Entity("border", null,
            new() { Entity("text", new() { ["text"] = "hi" }) }))!;
        border.Child.Should().BeOfType<TextBlock>();
    }

    [Fact]
    public void Separator_Creates()
        => CreateFactory().ConvertToControl(Entity("separator")).Should().BeOfType<Separator>();

    [Fact]
    public void Spacer_UsesProvidedSize()
    {
        var c = CreateFactory().ConvertToControl(Entity("spacer",
            new() { ["width"] = "30", ["height"] = "40" }))!;
        c.Width.Should().Be(30);
        c.Height.Should().Be(40);
    }

    [Fact]
    public void Spacer_Defaults_WhenNoSize()
    {
        var c = CreateFactory().ConvertToControl(Entity("spacer"))!;
        c.Width.Should().Be(10);
        c.Height.Should().Be(10);
    }

    [Fact]
    public void UnknownType_ReturnsNull()
        => CreateFactory().ConvertToControl(Entity("totally-unknown")).Should().BeNull();

    // ========== ApplyLayout：尺寸 ==========

    [Fact]
    public void ApplyLayout_WidthStar_SetsStretch()
    {
        var tb = new TextBlock { HorizontalAlignment = HorizontalAlignment.Left };
        CreateFactory().ApplyLayout(tb, new() { ["width"] = "*" }, 1000, 600);
        tb.HorizontalAlignment.Should().Be(HorizontalAlignment.Stretch);
    }

    [Fact]
    public void ApplyLayout_WidthPercent_SetsPixelWidth()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["width"] = "50%" }, 1000, 600);
        tb.Width.Should().Be(500);
    }

    [Fact]
    public void ApplyLayout_WidthPixels_SetsWidth()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["width"] = "200" }, 1000, 600);
        tb.Width.Should().Be(200);
    }

    [Fact]
    public void ApplyLayout_HeightPercent_SetsPixelHeight()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["height"] = "25%" }, 1000, 800);
        tb.Height.Should().Be(200);
    }

    // ========== ApplyLayout：定位 Margin ==========

    [Fact]
    public void ApplyLayout_MarginString_Parsed()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["margin"] = "10,5,0,0" }, 1000, 600);
        tb.Margin.Should().Be(new Thickness(10, 5, 0, 0));
    }

    [Fact]
    public void ApplyLayout_XY_LeftAlignDefault_AsMarginLeftTop()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["x"] = "100", ["y"] = "50" }, 1000, 600);
        tb.Margin.Left.Should().Be(100);
        tb.Margin.Top.Should().Be(50);
    }

    [Fact]
    public void ApplyLayout_XWithCenterAlign_UsesFormula()
    {
        // halign=center → marginLeft = 2*xPx - pw
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["halign"] = "center", ["x"] = "600" }, 1000, 600);
        tb.Margin.Left.Should().Be(2 * 600 - 1000);
    }

    [Fact]
    public void ApplyLayout_XWithRightAlign_UsesMarginRight()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["halign"] = "right", ["x"] = "800" }, 1000, 600);
        tb.Margin.Right.Should().Be(1000 - 800);
    }

    [Fact]
    public void ApplyLayout_RightBottom_SetsMargins()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["right"] = "50", ["bottom"] = "30" }, 1000, 600);
        tb.Margin.Right.Should().Be(50);
        tb.Margin.Bottom.Should().Be(30);
    }

    [Theory]
    [InlineData("right", HorizontalAlignment.Right)]
    [InlineData("center", HorizontalAlignment.Center)]
    [InlineData("left", HorizontalAlignment.Left)]
    public void ApplyLayout_Halign_MapsAlignment(string align, HorizontalAlignment expected)
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["halign"] = align }, 1000, 600);
        tb.HorizontalAlignment.Should().Be(expected);
    }

    [Fact]
    public void ApplyLayout_Valign_Bottom()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["valign"] = "bottom" }, 1000, 600);
        tb.VerticalAlignment.Should().Be(VerticalAlignment.Bottom);
    }

    [Fact]
    public void ApplyLayout_CanvasMode_XY_SetsCanvasPosition()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyLayout(tb, new() { ["x"] = "120", ["y"] = "80" }, 1000, 600, "canvas");
        Canvas.GetLeft(tb).Should().Be(120);
        Canvas.GetTop(tb).Should().Be(80);
    }

    // ========== ApplyCommonProps ==========

    [Fact]
    public void ApplyCommonProps_Opacity_Clamped()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyCommonProps(tb, new() { ["opacity"] = "2" });
        tb.Opacity.Should().Be(1); // clamp to 1
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void ApplyCommonProps_Visible_FromString(string vis, bool expected)
    {
        var tb = new TextBlock();
        CreateFactory().ApplyCommonProps(tb, new() { ["visible"] = vis });
        tb.IsVisible.Should().Be(expected);
    }

    [Fact]
    public void ApplyCommonProps_Enabled_False()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyCommonProps(tb, new() { ["enabled"] = "false" });
        tb.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void ApplyCommonProps_ZIndex_Applied()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyCommonProps(tb, new() { ["zindex"] = "7" });
        tb.GetValue(Panel.ZIndexProperty).Should().Be(7);
    }

    [Fact]
    public void ApplyCommonProps_ClipToBounds_True()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyCommonProps(tb, new() { ["clipToBounds"] = "true" });
        tb.ClipToBounds.Should().BeTrue();
    }

    [Fact]
    public void ApplyCommonProps_RenderTransform_RotationAndScale()
    {
        var tb = new TextBlock();
        CreateFactory().ApplyCommonProps(tb, new() { ["rotation"] = "45", ["scale"] = "2" });
        tb.RenderTransform.Should().BeOfType<TransformGroup>();
        var group = (TransformGroup)tb.RenderTransform!;
        group.Children.Should().Contain(t => t is RotateTransform);
        group.Children.Should().Contain(t => t is ScaleTransform);
    }

    [Fact]
    public void ApplyCommonProps_Border_CornerRadiusBrushThickness()
    {
        var border = new Border();
        CreateFactory().ApplyCommonProps(border, new()
        {
            ["cornerRadius"] = "8",
            ["borderBrush"] = "#FF0000",
            ["borderThickness"] = "2"
        });
        border.CornerRadius.Should().Be(new CornerRadius(8));
        ((SolidColorBrush)border.BorderBrush!).Color.Should().Be(Color.Parse("#FF0000"));
        border.BorderThickness.Should().Be(new Thickness(2));
    }

    // ========== RefreshBoundTextBlocks ==========

    [Fact]
    public void RefreshBoundTextBlocks_UpdatesTextWhenVariableChanges()
    {
        var state = new StateContainer();
        state.Set("score", 1);
        var factory = CreateFactory(state);

        var tb = (TextBlock)factory.ConvertToControl(Entity("text",
            new() { ["text"] = "Score: {score}" }))!;
        tb.Text.Should().Contain("1");
        factory.BoundTextBlocks.Should().ContainSingle();

        state.Set("score", 42);
        factory.RefreshBoundTextBlocks();
        tb.Text.Should().Contain("42");
    }
}
