using FluentAssertions;
using LingFanEngine.DslCore;
using Xunit;

namespace LingFanEngine.Tests.DslCore;

public class DslKeywordsTests
{
    [Fact]
    public void UiElementTypes_ContainsGrid()
    {
        DslKeywords.UiElementTypes.Should().Contain("grid");
        DslKeywords.UiElementTypes.Should().Contain("vbox");
        DslKeywords.UiElementTypes.Should().Contain("hbox");
    }

    [Fact]
    public void Statements_ContainsCoreKeywords()
    {
        DslKeywords.Statements.Should().Contain("say");
        DslKeywords.Statements.Should().Contain("set");
        DslKeywords.Statements.Should().Contain("if");
        DslKeywords.Statements.Should().Contain("scene");
        DslKeywords.Statements.Should().Contain("while");
    }

    [Fact]
    public void Parameters_ContainsKnownParams()
    {
        DslKeywords.Parameters.Should().Contain("speaker");
        DslKeywords.Parameters.Should().Contain("duration");
    }

    [Fact]
    public void ElementAttributes_And_Literals_Populated()
    {
        DslKeywords.ElementAttributes.Should().Contain("source");
        DslKeywords.Literals.Should().Contain("game");
        DslKeywords.Literals.Should().Contain("true");
    }

    [Fact]
    public void All_IsUnionOfCategories()
    {
        DslKeywords.All.Should().Contain("say");      // statements
        DslKeywords.All.Should().Contain("grid");     // ui element
        DslKeywords.All.Should().Contain("speaker");  // parameter
        DslKeywords.All.Should().Contain("game");     // literal
        DslKeywords.All.Should().Contain("class");    // element attribute
    }

    [Fact]
    public void All_IsSupersetOfEveryCategory()
    {
        // 行为校验：All 必须是各分类集合的并集（自动去重）。若某分类被遗漏于 All，
        // DSL 解析/高亮将缺词——这是 E 类「仅校验成员存在」无法发现的真实逻辑缺陷。
        DslKeywords.All.Should().Contain(DslKeywords.Statements);
        DslKeywords.All.Should().Contain(DslKeywords.Parameters);
        DslKeywords.All.Should().Contain(DslKeywords.ElementAttributes);
        DslKeywords.All.Should().Contain(DslKeywords.Literals);
        DslKeywords.All.Should().Contain(DslKeywords.UiElementTypes);
    }
}
