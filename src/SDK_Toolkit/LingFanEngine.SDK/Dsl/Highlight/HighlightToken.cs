namespace LingFanEngine.SDK.Dsl.Highlight;

/// <summary>高亮分类</summary>
public enum HighlightCategory
{
    Keyword,
    String,
    Comment,
    Variable,
    Label,
    Number,
    Symbol,
    Plain,
}

/// <summary>高亮 Token</summary>
public record HighlightToken(int Start, int Length, HighlightCategory Category);
