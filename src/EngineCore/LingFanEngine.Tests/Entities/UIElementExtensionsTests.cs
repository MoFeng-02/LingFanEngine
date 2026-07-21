using FluentAssertions;
using LingFanEngine.Abstractions.Entities.UIs;
using Xunit;

namespace LingFanEngine.Tests.Entities;

/// <summary>
/// UIElementExtensions 是纯 Fluent 语法糖，底层写 Properties 字典。
/// 这里逐方法验证「返回值即自身」+「写入的键/值正确」，并覆盖 double 重载的字符串格式化分支。
/// </summary>
public class UIElementExtensionsTests
{
    private static UIElementEntity NewElem() => new() { ElementType = "Text" };

    [Fact]
    public void At_SetsXAndY_AndReturnsSelf()
    {
        var e = NewElem();
        var r = e.At("10", "20");

        r.Should().BeSameAs(e);
        e.Properties["x"].Should().Be("10");
        e.Properties["y"].Should().Be("20");
    }

    [Fact]
    public void Size_StringOverload_SetsWidthAndHeight()
    {
        var e = NewElem();
        var r = e.Size("100", "200");

        r.Should().BeSameAs(e);
        e.Properties["width"].Should().Be("100");
        e.Properties["height"].Should().Be("200");
    }

    [Fact]
    public void Size_DoubleOverload_ConvertsToString()
    {
        var e = NewElem();
        e.Size(120.5, 240.0);

        e.Properties["width"].Should().Be("120.5");
        e.Properties["height"].Should().Be("240");
    }

    [Fact]
    public void Margin_SetsMargin()
    {
        var e = NewElem();
        e.Margin("4 8").Properties["margin"].Should().Be("4 8");
    }

    [Fact]
    public void Padding_SetsPadding()
    {
        var e = NewElem();
        e.Padding("2 4").Properties["padding"].Should().Be("2 4");
    }

    [Fact]
    public void FontSize_StringOverload_SetsFontSize()
    {
        var e = NewElem();
        e.FontSize("16").Properties["fontSize"].Should().Be("16");
    }

    [Fact]
    public void FontSize_DoubleOverload_ConvertsToString()
    {
        var e = NewElem();
        e.FontSize(14.0);
        e.Properties["fontSize"].Should().Be("14");
    }

    [Fact]
    public void Color_SetsColor()
    {
        var e = NewElem();
        e.Color("#ff0000").Properties["color"].Should().Be("#ff0000");
    }

    [Fact]
    public void Center_SetsTextAlignCenter()
    {
        var e = NewElem();
        e.Center().Properties["textAlign"].Should().Be("center");
    }

    [Fact]
    public void Font_SetsFont()
    {
        var e = NewElem();
        e.Font("serif").Properties["font"].Should().Be("serif");
    }

    [Fact]
    public void MaxWidth_StringOverload_SetsMaxWidth()
    {
        var e = NewElem();
        e.MaxWidth("80%").Properties["maxWidth"].Should().Be("80%");
    }

    [Fact]
    public void MaxWidth_DoubleOverload_ConvertsToString()
    {
        var e = NewElem();
        e.MaxWidth(300.0);
        e.Properties["maxWidth"].Should().Be("300");
    }

    [Fact]
    public void Nav_SetsNavPropertyAndCommand()
    {
        var e = NewElem();
        e.Nav("scene_a");

        e.Properties["nav"].Should().Be("scene_a");
        e.Command.Should().Be("scene_a");
    }

    [Fact]
    public void Cmd_WithoutValue_SetsCmdAndCommandOnly()
    {
        var e = NewElem();
        e.Cmd("hello");

        e.Properties["cmd"].Should().Be("hello");
        e.Command.Should().Be("hello");
        e.Properties.ContainsKey("value").Should().BeFalse();
        e.CommandValue.Should().BeNull();
    }

    [Fact]
    public void Cmd_WithValue_SetsValueAndCommandValue()
    {
        var e = NewElem();
        e.Cmd("hello", "world");

        e.Properties["cmd"].Should().Be("hello");
        e.Command.Should().Be("hello");
        e.Properties["value"].Should().Be("world");
        e.CommandValue.Should().Be("world");
    }

    [Fact]
    public void Opacity_StringOverload_SetsOpacity()
    {
        var e = NewElem();
        e.Opacity("0.5").Properties["opacity"].Should().Be("0.5");
    }

    [Fact]
    public void Opacity_DoubleOverload_UsesZeroHashHashFormat()
    {
        var e = NewElem();
        e.Opacity(1.0);
        e.Properties["opacity"].Should().Be("1");

        e.Opacity(0.5);
        e.Properties["opacity"].Should().Be("0.5");

        e.Opacity(0.25);
        e.Properties["opacity"].Should().Be("0.25");
    }

    [Fact]
    public void Order_SetsOrderProperty()
    {
        var e = NewElem();
        e.Order(5);
        e.Order.Should().Be(5);
    }

    [Fact]
    public void FluentChaining_AccumulatesAllProperties()
    {
        var e = NewElem()
            .At("1", "2")
            .Size("10", "20")
            .Color("red")
            .Center()
            .Font("serif")
            .MaxWidth(80.0)
            .Order(3);

        e.Properties["x"].Should().Be("1");
        e.Properties["y"].Should().Be("2");
        e.Properties["width"].Should().Be("10");
        e.Properties["height"].Should().Be("20");
        e.Properties["color"].Should().Be("red");
        e.Properties["textAlign"].Should().Be("center");
        e.Properties["font"].Should().Be("serif");
        e.Properties["maxWidth"].Should().Be("80");
        e.Order.Should().Be(3);
    }
}
