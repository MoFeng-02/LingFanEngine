namespace LingFanEngine.SDK.Models;

/// <summary>引用查找结果（P0-4 Find All References）</summary>
public record ReferenceResult(
    string FilePath,
    int Line,
    int Column,
    string LineText,
    ReferenceKind Kind);

/// <summary>引用类型</summary>
public enum ReferenceKind
{
    Variable,
    Scene,
    Label,
    Character,
    Style,
    Function,
    Sprite,
}
