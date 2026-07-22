using System.Collections.Generic;
using Avalonia;
using FluentAssertions;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// B1-a：LayoutHelper 纯静态数学测试（无宿主）。
/// <para>LayoutHelper 是 internal static 纯函数集合，经 InternalsVisibleTo 可直测；
/// 覆盖 ParseDouble/ParseInt/ResolveSize/ParseValueWithPercent/ParseThickness/
/// IsContainerType/ResolvePercentPosition/ResolvePercentSize 全部分支。</para>
/// </summary>
public class LayoutHelperTests
{
    private static Dictionary<string, object> P(params (string k, object v)[] items)
    {
        var d = new Dictionary<string, object>();
        foreach (var (k, v) in items) d[k] = v;
        return d;
    }

    // ========== ParseDouble ==========

    [Fact]
    public void ParseDouble_MissingOpacity_DefaultsToOne()
        => LayoutHelper.ParseDouble(P(), "opacity").Should().Be(1.0);

    [Theory]
    [InlineData("scale")]
    [InlineData("scaleX")]
    [InlineData("scaleY")]
    public void ParseDouble_MissingScale_DefaultsToOne(string key)
        => LayoutHelper.ParseDouble(P(), key).Should().Be(1.0);

    [Fact]
    public void ParseDouble_MissingOther_DefaultsToZero()
        => LayoutHelper.ParseDouble(P(), "width").Should().Be(0);

    [Theory]
    [InlineData(3.5, 3.5)]
    [InlineData(7, 7.0)]
    public void ParseDouble_NumericPassthrough(object val, double expected)
        => LayoutHelper.ParseDouble(P(("k", val)), "k").Should().Be(expected);

    [Theory]
    [InlineData("50%", 50)]
    [InlineData("640", 640)]
    [InlineData("not-a-number", 0)]
    public void ParseDouble_StringParsing(string s, double expected)
        => LayoutHelper.ParseDouble(P(("k", s)), "k").Should().Be(expected);

    // ========== ParseInt ==========

    [Fact]
    public void ParseInt_Missing_ReturnsNull()
        => LayoutHelper.ParseInt(P(), "col").Should().BeNull();

    [Theory]
    [InlineData(5, 5)]
    [InlineData("3", 3)]
    public void ParseInt_ValidValues(object val, int expected)
        => LayoutHelper.ParseInt(P(("col", val)), "col").Should().Be(expected);

    [Fact]
    public void ParseInt_DoubleTruncatesToInt()
        => LayoutHelper.ParseInt(P(("col", 2.9)), "col").Should().Be(2);

    [Fact]
    public void ParseInt_BadString_ReturnsNull()
        => LayoutHelper.ParseInt(P(("col", "x")), "col").Should().BeNull();

    // ========== ResolveSize ==========

    [Fact]
    public void ResolveSize_Missing_ReturnsZero()
        => LayoutHelper.ResolveSize(P(), "width", 1000).Should().Be(0);

    [Fact]
    public void ResolveSize_Percent_MultipliesParent()
        => LayoutHelper.ResolveSize(P(("width", "50%")), "width", 200).Should().Be(100);

    [Theory]
    [InlineData("640", 640)]
    [InlineData("bad", 0)]
    public void ResolveSize_StringNumberOrGarbage(string s, double expected)
        => LayoutHelper.ResolveSize(P(("width", s)), "width", 1000).Should().Be(expected);

    [Fact]
    public void ResolveSize_NumericPassthrough()
        => LayoutHelper.ResolveSize(P(("width", 320)), "width", 1000).Should().Be(320);

    // ========== ParseValueWithPercent ==========

    [Theory]
    [InlineData("25%", 25, true)]
    [InlineData("120", 120, false)]
    [InlineData("junk", 0, false)]
    public void ParseValueWithPercent_Cases(string s, double expectedVal, bool expectedPct)
    {
        var (value, isPercent) = LayoutHelper.ParseValueWithPercent(s);
        value.Should().Be(expectedVal);
        isPercent.Should().Be(expectedPct);
    }

    [Fact]
    public void ParseValueWithPercent_Numeric_NotPercent()
    {
        var (value, isPercent) = LayoutHelper.ParseValueWithPercent(48.0);
        value.Should().Be(48.0);
        isPercent.Should().BeFalse();
    }

    // ========== ParseThickness ==========

    [Fact]
    public void ParseThickness_SingleValue_Uniform()
        => LayoutHelper.ParseThickness("10").Should().Be(new Thickness(10));

    [Fact]
    public void ParseThickness_FourValues()
        => LayoutHelper.ParseThickness("10,5,0,0").Should().Be(new Thickness(10, 5, 0, 0));

    [Fact]
    public void ParseThickness_Invalid_ReturnsZero()
        => LayoutHelper.ParseThickness("a,b").Should().Be(new Thickness(0));

    // ========== IsContainerType ==========

    [Theory]
    [InlineData("panel")]
    [InlineData("grid")]
    [InlineData("stack")]
    [InlineData("canvas")]
    [InlineData("border")]
    [InlineData("scroll")]
    public void IsContainerType_KnownContainers_True(string type)
        => LayoutHelper.IsContainerType(type).Should().BeTrue();

    [Theory]
    [InlineData("text")]
    [InlineData("button")]
    [InlineData("image")]
    public void IsContainerType_NonContainers_False(string type)
        => LayoutHelper.IsContainerType(type).Should().BeFalse();

    // ========== ResolvePercentPosition / ResolvePercentSize ==========

    [Fact]
    public void ResolvePercentPosition_Percent_ScalesToParent()
        => LayoutHelper.ResolvePercentPosition("25%", 1280).Should().Be(320);

    [Fact]
    public void ResolvePercentPosition_Absolute_ReturnsRaw()
        => LayoutHelper.ResolvePercentPosition("100", 1280).Should().Be(100);

    [Fact]
    public void ResolvePercentSize_Null_ReturnsNaN()
        => double.IsNaN(LayoutHelper.ResolvePercentSize(null, 1000)).Should().BeTrue();

    [Fact]
    public void ResolvePercentSize_Percent_ScalesToParent()
        => LayoutHelper.ResolvePercentSize("50%", 800).Should().Be(400);

    [Fact]
    public void ResolvePercentSize_Absolute_ReturnsRaw()
        => LayoutHelper.ResolvePercentSize("250", 800).Should().Be(250);
}
