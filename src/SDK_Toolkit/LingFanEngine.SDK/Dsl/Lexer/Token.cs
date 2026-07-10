namespace LingFanEngine.SDK.Dsl.Lexer;

/// <summary>词法单元</summary>
public record Token(TokenType Type, string Value, int Line, int Column);
