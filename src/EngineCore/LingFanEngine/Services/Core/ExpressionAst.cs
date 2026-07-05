namespace LingFanEngine.Services.Core;

/// <summary>
/// 表达式 AST 抽象基类
/// </summary>
public abstract partial class Expr
{
}

/// <summary>
/// 字面量表达式——数字、字符串、布尔、null
/// </summary>
public sealed partial class LiteralExpr : Expr
{
    public object? Value { get; }

    public LiteralExpr(object? value)
    {
        Value = value;
    }

    public override string ToString() => Value?.ToString() ?? "null";
}

/// <summary>
/// 变量引用表达式——支持点号路径 player.stats.hp
/// </summary>
public sealed partial class VariableExpr : Expr
{
    public string Path { get; }

    public VariableExpr(string path)
    {
        Path = path;
    }

    public override string ToString() => Path;
}

/// <summary>
/// 二元运算表达式
/// </summary>
public sealed partial class BinaryExpr : Expr
{
    public Expr Left { get; }
    public string Op { get; }
    public Expr Right { get; }

    public BinaryExpr(Expr left, string op, Expr right)
    {
        Left = left;
        Op = op;
        Right = right;
    }

    public override string ToString() => $"({Left} {Op} {Right})";
}

/// <summary>
/// 一元运算表达式——! -
/// </summary>
public sealed partial class UnaryExpr : Expr
{
    public string Op { get; }
    public Expr Operand { get; }

    public UnaryExpr(string op, Expr operand)
    {
        Op = op;
        Operand = operand;
    }

    public override string ToString() => $"({Op}{Operand})";
}

/// <summary>
/// 函数调用表达式——random(1, 6)、min(a, b)、自定义函数
/// </summary>
public sealed partial class FunctionCallExpr : Expr
{
    public string FunctionName { get; }
    public IReadOnlyList<Expr> Arguments { get; }

    public FunctionCallExpr(string functionName, IReadOnlyList<Expr> arguments)
    {
        FunctionName = functionName;
        Arguments = arguments;
    }

    public override string ToString() => $"{FunctionName}({string.Join(", ", Arguments)})";
}

/// <summary>
/// 三元条件表达式——cond ? a : b
/// </summary>
public sealed partial class ConditionalExpr : Expr
{
    public Expr Condition { get; }
    public Expr ThenExpr { get; }
    public Expr ElseExpr { get; }

    public ConditionalExpr(Expr condition, Expr thenExpr, Expr elseExpr)
    {
        Condition = condition;
        ThenExpr = thenExpr;
        ElseExpr = elseExpr;
    }

    public override string ToString() => $"({Condition} ? {ThenExpr} : {ElseExpr})";
}
