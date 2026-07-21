using FluentAssertions;
using LingFanEngine.Services.Core;
using System.Text.Json;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// JsonValueConverter 测试：Convert 对 JsonElement（数字/布尔/字符串/数组/对象/Null）及非 JsonElement 的还原。
/// <para>同时验证 RegisterCustomConverter 的优先级（自定义转换器插在默认链最前）。</para>
/// </summary>
public class JsonValueConverterTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Convert_IntNumber_ReturnsInt()
    {
        var c = new JsonValueConverter();
        c.Convert(Parse("123")).Should().Be(123);
    }

    [Fact]
    public void Convert_LongNumber_ReturnsLong()
    {
        var c = new JsonValueConverter();
        c.Convert(Parse("9000000000")).Should().Be(9000000000L);
    }

    [Fact]
    public void Convert_DoubleNumber_ReturnsDouble()
    {
        var c = new JsonValueConverter();
        c.Convert(Parse("3.14")).Should().Be(3.14);
    }

    [Fact]
    public void Convert_True_ReturnsTrue()
    {
        var c = new JsonValueConverter();
        c.Convert(Parse("true")).Should().Be(true);
    }

    [Fact]
    public void Convert_False_ReturnsFalse()
    {
        var c = new JsonValueConverter();
        c.Convert(Parse("false")).Should().Be(false);
    }

    [Fact]
    public void Convert_String_ReturnsString()
    {
        var c = new JsonValueConverter();
        c.Convert(Parse("\"hello\"")).Should().Be("hello");
    }

    [Fact]
    public void Convert_Null_ReturnsNull()
    {
        var c = new JsonValueConverter();
        c.Convert(Parse("null")).Should().BeNull();
    }

    [Fact]
    public void Convert_Array_ReturnsList()
    {
        var c = new JsonValueConverter();
        var result = c.Convert(Parse("[1, 2, 3]"));
        result.Should().BeOfType<List<object?>>();
        var list = (List<object?>)result!;
        list.Should().HaveCount(3);
        list[0].Should().Be(1);
        list[2].Should().Be(3);
    }

    [Fact]
    public void Convert_Object_ReturnsDictionary()
    {
        var c = new JsonValueConverter();
        var result = c.Convert(Parse("{\"a\":1,\"b\":\"x\"}"));
        result.Should().BeOfType<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)result!;
        dict["a"].Should().Be(1);
        dict["b"].Should().Be("x");
    }

    [Fact]
    public void Convert_NonJsonElement_ReturnsValueUnchanged()
    {
        var c = new JsonValueConverter();
        c.Convert("hello").Should().Be("hello");
        c.Convert(42).Should().Be(42);
        c.Convert((object?)null).Should().BeNull();
    }

    [Fact]
    public void RegisterCustomConverter_TakesPrecedence_OverDefault()
    {
        var c = new JsonValueConverter();
        // 默认行为：字符串 "five" 原样返回
        c.Convert(Parse("\"five\"")).Should().Be("five");

        // 注册自定义转换器：将 "five" 映射为 5
        c.RegisterCustomConverter(je =>
            je.ValueKind == JsonValueKind.String && je.GetString() == "five" ? 5 : null);

        c.Convert(Parse("\"five\"")).Should().Be(5);
        // 其他值仍走默认链
        c.Convert(Parse("\"other\"")).Should().Be("other");
        c.Convert(Parse("7")).Should().Be(7);
    }
}
