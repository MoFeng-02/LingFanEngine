﻿﻿﻿﻿using System.Collections.Concurrent;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// 标签跳转命令——DSL 中 label/jump 编译结果
/// </summary>
public readonly record struct JumpCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>目标标签名</summary>
    public required string TargetLabel { get; init; }

    /// <summary>要跳转到的命令索引（编译时填入，运行时直接跳转）</summary>
    public int TargetIndex { get; init; } = -1;

    public JumpCommand() { }
}

/// <summary>
/// 条件分支命令——DSL 中 if/elif/else 编译结果
/// <para>编译时记录条件表达式和跳过指令数，运行时由 DslExecutor 求值决定是否跳过。</para>
/// </summary>
public readonly record struct BranchCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>
    /// 条件表达式文本（如 "gold >= 100"），仅 if/elif 有值；else 和 end 为 null
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// 跳过指令数：条件不满足时跳过的后续指令数量
    /// <para>if {cond} → 不满足时跳过 if 块（含 elif/else）直到 end</para>
    /// <para>elif {cond} → 不满足时跳过 elif 块</para>
    /// <para>else → 无条件跳过（因为前面的条件已满足）</para>
    /// <para>end → 不跳过</para>
    /// </summary>
    public int SkipCount { get; init; }

    /// <summary>
    /// 是否已经匹配（用于 elif 链：前面的 if 或 elif 已经满足）
    /// </summary>
    public bool HasMatched { get; init; }

    public BranchCommand() { }
}

/// <summary>
/// 菜单命令——DSL 中 menu {} 编译结果
/// </summary>
public readonly record struct MenuCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>菜单提示文本</summary>
    public string? Prompt { get; init; }

    /// <summary>选项列表（文本 → 目标标签）</summary>
    public required IReadOnlyList<(string Text, string TargetLabel)> Options { get; init; }

    public MenuCommand() { }
}

/// <summary>
/// 场景构建命令——DSL 中 scene {} 编译结果
/// <para>包含完整的 UIElementEntity 列表，GameLoop 消费时写入状态容器。</para>
/// </summary>
public readonly record struct BuildSceneCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>场景名</summary>
    public string? SceneName { get; init; }

    /// <summary>场景元素列表</summary>
    public required List<object> RawElements { get; init; }

    public BuildSceneCommand() { }
}

/// <summary>
/// 过渡动画命令——DSL 中 transition "fade" duration=0.5 编译结果
/// </summary>
public readonly record struct TransitionCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>过渡类型标识符（如 "FadeIn", "SlideLeftIn"）</summary>
    public required string Type { get; init; }

    /// <summary>过渡持续时间（秒）</summary>
    public double Duration { get; init; } = 0.5;

    public TransitionCommand() { }
}

/// <summary>
/// 等待命令——DSL 中 wait 2.0 编译结果
/// </summary>
public readonly record struct WaitCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>等待秒数</summary>
    public required double Seconds { get; init; }

    public WaitCommand() { }
}

/// <summary>
/// 显示/隐藏命令——DSL 中 show/hide 编译结果
/// </summary>
public readonly record struct ShowHideCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>资源路径或元素标识</summary>
    public required string Target { get; init; }

    /// <summary>X 坐标（仅 show 有效）</summary>
    public double X { get; init; }

    /// <summary>Y 坐标（仅 show 有效）</summary>
    public double Y { get; init; }

    /// <summary>是否为显示操作（false = hide）</summary>
    public bool IsShow { get; init; } = true;

    /// <summary>是否为背景操作（background 命令）</summary>
    public bool IsBackground { get; init; }

    /// <summary>元素标签（按 tag 匹配 hide，对标 Ren'Py image tag）</summary>
    public string? Tag { get; init; }

    public ShowHideCommand() { }
}

/// <summary>
/// 输入命令——DSL 中 input 编译结果
/// </summary>
public readonly record struct InputCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>输入提示文本</summary>
    public required string Prompt { get; init; }

    /// <summary>存储输入的变量键名</summary>
    public required string StoreKey { get; init; }

    /// <summary>可选的选项列表（多项选择）</summary>
    public string[]? Options { get; init; }

    public InputCommand() { }
}

/// <summary>
/// 存档命令——DSL 中 save/load 编译结果
/// </summary>
public readonly record struct SaveLoadCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>存档槽标识</summary>
    public required string SlotId { get; init; }

    /// <summary>是否为保存操作（false = 加载）</summary>
    public bool IsSave { get; init; } = true;

    public SaveLoadCommand() { }
}

/// <summary>
/// 表达式命令——延时求值的变量引用或算术表达式
/// <para>DSL 中 {gold + 50} 编译为表达式节点，运行时由表达式求值器处理。</para>
/// </summary>
public readonly record struct EvalCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>表达式文本（如 "gold + 50"）</summary>
    public required string Expression { get; init; }

    public EvalCommand() { }
}

// ========== 表达式求值器（AOT 安全，无反射） ==========

/// <summary>
/// 轻量表达式求值器
/// <para>支持 {变量} 引用、{a + b} 算术、random(min, max)、比较运算。</para>
/// </summary>
public static class DslExpressionEvaluator
{
    /// <summary>
    /// 表达式 AST 编译缓存——避免每次求值重新解析
    /// </summary>
    private static readonly ConcurrentDictionary<string, Expr?> _astCache = new();

    /// <summary>
    /// 求值 DSL 表达式（基于 Pidgin AST 引擎，带编译缓存）
    /// </summary>
    /// <param name="expr">表达式文本（不含花括号）</param>
    /// <param name="state">状态容器，用于读取变量</param>
    /// <returns>求值结果</returns>
    public static object? Evaluate(string expr, IStateContainer state)
    {
        if (string.IsNullOrWhiteSpace(expr))
            return null;

        expr = expr.Trim();

        // 从缓存获取或编译 AST
        var ast = _astCache.GetOrAdd(expr, e =>
        {
            var result = DslExpressionParser.Parse(e);
            return result.Success ? result.Value : null;
        });

        if (ast != null)
            return ExpressionEvaluator.Evaluate(ast, state);

        // Pidgin 解析失败，回退到旧引擎
        return EvaluateLegacy(expr, state);
    }

    /// <summary>
    /// 旧版求值——Pidgin 解析失败时的回退（不使用正则）
    /// </summary>
    /// <param name="expr">表达式文本（不含花括号）</param>
    /// <param name="state">状态容器，用于读取变量</param>
    /// <returns>求值结果</returns>
    private static object? EvaluateLegacy(string expr, IStateContainer state)
    {
        if (string.IsNullOrWhiteSpace(expr))
            return null;

        expr = expr.Trim();

        // 纯布尔
        if (expr == "true") return true;
        if (expr == "false") return false;
        if (expr == "null") return null;

        // random(min, max) 函数——简单字符串解析
        if (expr.StartsWith("random(") && expr.EndsWith(')'))
        {
            var args = expr[7..^1].Split(',');
            if (args.Length == 2 && int.TryParse(args[0].Trim(), out var min) && int.TryParse(args[1].Trim(), out var max))
                return Random.Shared.Next(min, max + 1);
        }

        // 纯变量引用：仅含字母/数字/下划线/点
        if (IsPureVariable(expr))
            return state.Get<object>(expr);

        // 比较表达式：gold >= 100
        var (cmpLeft, cmpOp, cmpRight) = TryParseComparison(expr);
        if (cmpOp != null)
        {
            var left = ResolveValue(cmpLeft.Trim(), state);
            var right = ResolveValue(cmpRight.Trim(), state);
            return CompareValues(left, right, cmpOp);
        }

        // 算术表达式：gold + 50
        var (arithLeft, arithOp, arithRight) = TryParseArithmetic(expr);
        if (arithOp != null)
        {
            var left = ResolveValue(arithLeft.Trim(), state);
            var right = ResolveValue(arithRight.Trim(), state);
            return ArithValues(left, right, arithOp);
        }

        // 纯数字
        if (double.TryParse(expr, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
        {
            if (num == (int)num) return (int)num;
            return num;
        }

        // 字符串
        if (expr.StartsWith('"') && expr.EndsWith('"'))
            return expr[1..^1];

        return expr;
    }

    /// <summary>
    /// 求值布尔表达式（基于 Pidgin AST 引擎）——用于 if/elif 条件判断
    /// <para>与 Evaluate 不同，此方法始终返回 bool。</para>
    /// </summary>
    public static bool EvaluateBool(string expr, IStateContainer state)
    {
        if (string.IsNullOrWhiteSpace(expr))
            return false;

        expr = expr.Trim();

        // 从缓存获取或编译 AST
        var ast = _astCache.GetOrAdd(expr, e =>
        {
            var result = DslExpressionParser.Parse(e);
            return result.Success ? result.Value : null;
        });

        if (ast != null)
            return ExpressionEvaluator.EvaluateBool(ast, state);

        // Pidgin 解析失败，回退到旧引擎
        return EvaluateBoolLegacy(expr, state);
    }

    /// <summary>
    /// 旧版布尔求值——Pidgin 解析失败时的回退（不使用正则）
    /// <para>与 Evaluate 不同，此方法始终返回 bool。</para>
    /// </summary>
    private static bool EvaluateBoolLegacy(string expr, IStateContainer state)
    {
        if (string.IsNullOrWhiteSpace(expr))
            return false;

        expr = expr.Trim();

        // 纯布尔
        if (expr == "true") return true;
        if (expr == "false") return false;

        // 比较表达式
        var (cmpLeft, cmpOp, cmpRight) = TryParseComparison(expr);
        if (cmpOp != null)
        {
            var left = ResolveValue(cmpLeft.Trim(), state);
            var right = ResolveValue(cmpRight.Trim(), state);
            return CompareValues(left, right, cmpOp);
        }

        // 纯变量引用：非空且非 false 即为真
        if (IsPureVariable(expr))
        {
            var val = state.Get<object>(expr);
            if (val == null) return false;
            if (val is bool b) return b;
            if (val is int i) return i != 0;
            if (val is double d) return d != 0;
            if (val is string s) return !string.IsNullOrEmpty(s);
            return true;
        }

        // 默认 false（未知表达式）
        return false;
    }

    /// <summary>判断是否为纯变量名（字母/数字/下划线/点）</summary>
    private static bool IsPureVariable(string expr)
    {
        if (string.IsNullOrEmpty(expr)) return false;
        foreach (var c in expr)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                return false;
        }
        return char.IsLetter(expr[0]) || expr[0] == '_';
    }

    /// <summary>尝试解析比较表达式，返回 (左, 运算符, 右) 或 (null, null, null)</summary>
    private static (string Left, string? Op, string Right) TryParseComparison(string expr)
    {
        string[] ops = [">=", "<=", "!=", "==", ">", "<"];
        foreach (var op in ops)
        {
            var idx = expr.IndexOf(op, StringComparison.Ordinal);
            if (idx > 0 && idx < expr.Length - op.Length)
            {
                return (expr[..idx], op, expr[(idx + op.Length)..]);
            }
        }
        return (expr, null, expr);
    }

    /// <summary>尝试解析算术表达式，返回 (左, 运算符, 右) 或 (null, null, null)</summary>
    private static (string Left, string? Op, string Right) TryParseArithmetic(string expr)
    {
        string[] ops = ["+", "-", "*", "/", "%"];
        foreach (var op in ops)
        {
            var idx = expr.IndexOf(op, StringComparison.Ordinal);
            if (idx > 0 && idx < expr.Length - 1)
            {
                return (expr[..idx], op, expr[(idx + 1)..]);
            }
        }
        return (expr, null, expr);
    }

    /// <summary>
    /// 解析值——变量引用从状态容器读取，否则返回自身
    /// </summary>
    private static object? ResolveValue(string token, IStateContainer state)
    {
        // 纯数字
        if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
            return num;

        // 布尔
        if (token == "true") return true;
        if (token == "false") return false;

        // 字符串
        if (token.StartsWith('"') && token.EndsWith('"'))
            return token[1..^1];

        // 变量引用：从状态容器读取
        return state.Get<object>(token);
    }

    private static double ToDouble(object? val) =>
        Convert.ToDouble(val ?? 0, System.Globalization.CultureInfo.InvariantCulture);

    private static object? ArithValues(object? left, object? right, string op)
    {
        var l = ToDouble(left);
        var r = ToDouble(right);

        double result = op switch
        {
            "+" => l + r,
            "-" => l - r,
            "*" => l * r,
            "/" => r != 0 ? l / r : 0,
            "%" => r != 0 ? l % r : 0,
            _ => 0
        };

        // 如果结果是整数则返回 int
        if (result == (int)result) return (int)result;
        return result;
    }

    private static bool CompareValues(object? left, object? right, string op)
    {
        var l = ToDouble(left);
        var r = ToDouble(right);

        return op switch
        {
            "==" => l == r,
            "!=" => l != r,
            ">" => l > r,
            ">=" => l >= r,
            "<" => l < r,
            "<=" => l <= r,
            _ => false
        };
    }

    /// <summary>
    /// 注册自定义 DSL 函数（转发到 ExpressionEvaluator）
    /// </summary>
    /// <param name="name">函数名（不区分大小写）</param>
    /// <param name="func">函数实现</param>
    public static void RegisterFunction(string name, DslFunction func)
        => ExpressionEvaluator.RegisterFunction(name, func);

    /// <summary>
    /// 注销自定义 DSL 函数
    /// </summary>
    public static bool UnregisterFunction(string name)
        => ExpressionEvaluator.UnregisterFunction(name);

    /// <summary>
    /// 替换字符串中的所有 {表达式} 为求值结果
    /// </summary>
    public static string ReplaceText(string text, IStateContainer state)
    {
        int pos = 0;
        while (pos < text.Length && (pos = text.IndexOf('{', pos)) >= 0)
        {
            var end = text.IndexOf('}', pos);
            if (end < 0) break;

            var expr = text[(pos + 1)..end].Trim();

            // 跳过标记标签，留给 DialogBox.ApplyInlineMarkup 处理
            if (IsMarkupTag(expr))
            {
                pos = end + 1;
                continue;
            }

            // 格式化语法 {var:format}——变量名后跟冒号和格式字符串
            // 注意：三元表达式 a ? b : c 也含冒号，但三元含 ? 号，故以此区分
            var colonIdx = expr.IndexOf(':');
            string replacement;
            if (colonIdx > 0 && !expr.Contains('?'))
            {
                var varName = expr[..colonIdx].Trim();
                var format = expr[(colonIdx + 1)..].Trim();
                var varValue = Evaluate(varName, state);
                replacement = varValue switch
                {
                    IFormattable f => f.ToString(format, System.Globalization.CultureInfo.InvariantCulture),
                    null => "",
                    _ => varValue.ToString() ?? ""
                };
            }
            else
            {
                var value = Evaluate(expr, state);
                replacement = value?.ToString() ?? "";
            }

            text = text[..pos] + replacement + text[(end + 1)..];
            pos += replacement.Length;
        }
        return text;
    }

    private static bool IsMarkupTag(string expr)
    {
        var rawTag = expr.Contains('=') ? expr[..expr.IndexOf('=')] : expr;
        return rawTag switch
        {
            "b" or "/b" or "i" or "/i" or "u" or "/u"
                or "color" or "/color" or "font" or "/font" or "size" or "/size"
                or "w" or "fast" or "p" => true,
            _ => false
        };
    }

    // 正则回退路径已移除，替换为纯字符串解析
}

/// <summary>
/// 导航到标签——按钮交互产生的命令，GameLoop 收到后调 DslExecutor.StartFromLabel
/// <para>不通过 DslExecutor.Step() 处理，因为按钮点击可能始于 DslExecutor 未执行的状态。</para>
/// </summary>
public readonly record struct NavToLabelCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>目标标签名</summary>
    public required string TargetLabel { get; init; }

    public NavToLabelCommand() { }
}

/// <summary>
/// 场景命令——清空 SceneStack 并切换到新场景
/// <para>DSL 中 scene "xxx" 对应此命令，navigate 不改变堆栈。</para>
/// </summary>
public readonly record struct SceneCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>目标场景名</summary>
    public required string SceneName { get; init; }

    public SceneCommand() { }
}

/// <summary>
/// 回退命令——弹出 SceneStack 回到前一个场景
/// <para>DSL 中 back 命令对应此命令。</para>
/// </summary>
public readonly record struct BackCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public BackCommand() { }
}

/// <summary>
/// 前进命令——恢复之前后退时弹出的堆栈状态
/// </summary>
public readonly record struct ForwardCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public ForwardCommand() { }
}

/// <summary>
/// 回溯到指定检查点——从历史面板跳转到某句 Say
/// <para>targetCheckpointIndex 为 -1 时等价于 BackCommand。</para>
/// </summary>
public readonly record struct RollbackToCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public int TargetCheckpointIndex { get; init; } = -1;
    public RollbackToCommand() { }
}

/// <summary>
/// call label——调用子过程，保存返回位置
/// </summary>
public readonly record struct CallCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public string TargetLabel { get; init; } = "";
    public CallCommand() { }
}

/// <summary>
/// return——从 call 返回
/// </summary>
public readonly record struct ReturnCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public ReturnCommand() { }
}

/// <summary>
/// animate 控件级动画——移动/缩放/透明度等
/// </summary>
public readonly record struct AnimateCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public string Target { get; init; } = "";
    public string Property { get; init; } = "";
    public double TargetValue { get; init; }
    public double Duration { get; init; } = 1.0;
    public string Easing { get; init; } = "EaseOutQuad";
    /// <summary>循环次数（-1=无限，0=不循环，N=循环N次）</summary>
    public int RepeatCount { get; init; }
    public AnimateCommand() { }
}

/// <summary>
/// 屏幕震动命令——让画面产生短暂的抖动效果
/// <para>对标 Ren'Py 的 with hpunch / vpunch。</para>
/// </summary>
public readonly record struct ShakeCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>震动强度（像素，默认 10）</summary>
    public double Intensity { get; init; } = 10.0;

    /// <summary>震动持续时间（秒，默认 0.5）</summary>
    public double Duration { get; init; } = 0.5;

    public ShakeCommand() { }
}

/// <summary>
/// 跳过模式切换命令——开启/关闭跳过模式
/// </summary>
public readonly record struct ToggleSkipCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public ToggleSkipCommand() { }
}

/// <summary>
/// 自动模式切换命令——开启/关闭自动模式
/// </summary>
public readonly record struct ToggleAutoCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public ToggleAutoCommand() { }
}

/// <summary>
/// CG 解锁命令——DSL 中 gallery unlock "id" "path" 编译结果
/// <para>将 CG 标记为已解锁，可在鉴赏界面查看。</para>
/// </summary>
public readonly record struct UnlockGalleryCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>CG 唯一标识符</summary>
    public required string Id { get; init; }

    /// <summary>CG 图片路径</summary>
    public required string ImagePath { get; init; }

    /// <summary>CG 标题（可选）</summary>
    public string? Title { get; init; }

    /// <summary>关联场景名（可选，回想去该场景）</summary>
    public string? SceneName { get; init; }

    public UnlockGalleryCommand() { }
}

/// <summary>
/// 调试日志命令——DSL 中 debug "message" [level=Info] 编译结果
/// <para>记录调试信息到调试控制台。</para>
/// </summary>
public readonly record struct DebugLogCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>日志消息</summary>
    public required string Message { get; init; }

    /// <summary>日志级别（Info/Warning/Error/Debug）</summary>
    public string Level { get; init; } = "Info";

    public DebugLogCommand() { }
}

/// <summary>
/// NVL 模式命令——DSL 中 nvl / nvl clear 编译结果
/// <para>nvl：进入 NVL 模式，后续对话累积显示。</para>
/// <para>nvl clear：清空 NVL 累积文本并退出 NVL 模式。</para>
/// </summary>
public readonly record struct NvlCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>是否为清空操作（false=进入NVL模式，true=清空并退出）</summary>
    public bool IsClear { get; init; }

    public NvlCommand() { }
}

/// <summary>
/// 显示场景元素命令——scene 块内的 UI 元素行编译结果
/// <para>由 DslExecutor 同步执行：追加到 Scene.Elements + 设置 Dirty。</para>
/// <para>非阻塞——立即执行，不等待。阻塞由后续的 say/transition 等命令实现。</para>
/// </summary>
public readonly record struct ShowElementCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>要显示的 UI 元素</summary>
    public required UIElementEntity Element { get; init; }

    public ShowElementCommand() { }
}
