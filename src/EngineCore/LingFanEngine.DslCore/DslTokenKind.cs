namespace LingFanEngine.DslCore;

/// <summary>
/// DSL 词法 token 类型——tokenizer（<c>LingFanEngine.Dsl.LanguageService</c>）产出的基础分类。
/// 语义高亮类别（控制流 / 导航 / 容器型 UI 等）由 <see cref="DslKeywords"/> 子集与
/// <see cref="DslUiElementCategories"/> 等数据在语言服务层派生，不在此枚举内。
/// </summary>
public enum DslTokenKind
{
    /// <summary>未识别 / 兜底。</summary>
    Unknown = 0,

    /// <summary>注释（# 或 // 行）。</summary>
    Comment,

    /// <summary>数字字面量。</summary>
    Number,

    /// <summary>字符串字面量（含引号）。</summary>
    String,

    /// <summary>标识符（非关键字名称）。</summary>
    Identifier,

    /// <summary>关键字（语句首词或参数键，具体语义子类由语言服务层判定）。</summary>
    Keyword,

    /// <summary>符号（= , -&gt; { } ( ) 等）。</summary>
    Symbol,

    /// <summary>换行。</summary>
    Newline,

    /// <summary>空白（空格 / 制表符，不被高亮）。</summary>
    Whitespace,
}
