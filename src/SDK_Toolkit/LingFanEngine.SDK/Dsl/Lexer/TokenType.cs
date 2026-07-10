namespace LingFanEngine.SDK.Dsl.Lexer;

/// <summary>Token 类型</summary>
public enum TokenType
{
    /// <summary>关键字（scene/label/say/set/if/while/menu/navigate/jump 等）</summary>
    Keyword,

    /// <summary>标识符</summary>
    Identifier,

    /// <summary>双引号字符串</summary>
    String,

    /// <summary>数字（整数或浮点数）</summary>
    Number,

    /// <summary>符号（= : -> { } ( ) , 等）</summary>
    Symbol,

    /// <summary>注释（# 开头的行）</summary>
    Comment,

    /// <summary>换行</summary>
    Newline,

    /// <summary>未知 token</summary>
    Unknown,
}
