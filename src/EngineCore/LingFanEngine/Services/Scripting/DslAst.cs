namespace LingFanEngine.Services.Scripting;

/// <summary>
/// DSL 语句 AST 抽象基类
/// </summary>
public abstract partial class DslStatement
{
    /// <summary>原始行号（0-based）</summary>
    public int LineNumber { get; set; }
}

// ====== 控制流 ======

public sealed partial class LabelStmt : DslStatement
{
    public required string Name { get; init; }
}

public sealed partial class JumpStmt : DslStatement
{
    public required string TargetLabel { get; init; }
}

public sealed partial class CallStmt : DslStatement
{
    public required string TargetLabel { get; init; }
}

public sealed partial class ReturnStmt : DslStatement { }

// ====== 对话与导航 ======

public sealed partial class SayStmt : DslStatement
{
    public required string Text { get; init; }
    public string? Speaker { get; init; }
}

public sealed partial class NavigateStmt : DslStatement
{
    public required string Path { get; init; }
    public string? SceneName { get; init; }
}

public sealed partial class SceneStmt : DslStatement
{
    public required string SceneName { get; init; }
}

public sealed partial class BackStmt : DslStatement { }

public sealed partial class ForwardStmt : DslStatement { }

// ====== 变量操作 ======

public sealed partial class SetStmt : DslStatement
{
    public required string Key { get; init; }
    public required string ValuePart { get; init; }
}

public sealed partial class DefineStmt : DslStatement
{
    public required string Key { get; init; }
    public required string ValuePart { get; init; }
}

public sealed partial class LetStmt : DslStatement
{
    public required string Key { get; init; }
    public required string ValuePart { get; init; }
}

// ====== 媒体 ======

public sealed partial class BgmStmt : DslStatement
{
    public required string Path { get; init; }
    public float? Volume { get; init; }
}

public sealed partial class BackgroundStmt : DslStatement
{
    public required string Path { get; init; }
}

public sealed partial class ShowStmt : DslStatement
{
    public required string Target { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
}

public sealed partial class HideStmt : DslStatement
{
    public required string Target { get; init; }
}

public sealed partial class AnimateStmt : DslStatement
{
    public required string Target { get; init; }
    public required string Property { get; init; }
    public required double TargetValue { get; init; }
    public double? Duration { get; init; }
    public string? Easing { get; init; }
}

// ====== 流程控制 ======

public sealed partial class WaitStmt : DslStatement
{
    public required double Seconds { get; init; }
}

public sealed partial class TransitionStmt : DslStatement
{
    public required string Type { get; init; }
    public double? Duration { get; init; }
}

public sealed partial class InputStmt : DslStatement
{
    public required string Prompt { get; init; }
    public required string StoreKey { get; init; }
    public string[]? Options { get; init; }
}

public sealed partial class MenuStmt : DslStatement
{
    public required string Prompt { get; init; }
}

public sealed partial class SaveStmt : DslStatement
{
    public required string SlotId { get; init; }
}

public sealed partial class LoadStmt : DslStatement
{
    public required string SlotId { get; init; }
}

// ====== 条件/循环块结构（行级标记）======

public sealed partial class IfStmt : DslStatement
{
    public required string Condition { get; init; }
}

public sealed partial class ElseIfStmt : DslStatement
{
    public required string Condition { get; init; }
}

public sealed partial class ElseStmt : DslStatement { }

public sealed partial class WhileStmt : DslStatement
{
    public required string Condition { get; init; }
}

public sealed partial class EndBlockStmt : DslStatement { }

public sealed partial class EndStmt : DslStatement { }
