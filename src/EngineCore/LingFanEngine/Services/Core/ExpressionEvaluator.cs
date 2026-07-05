using System.Collections.Concurrent;
using System.Globalization;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// DSL 自定义函数委托
/// </summary>
/// <param name="args">参数列表（已求值）</param>
/// <param name="state">状态容器</param>
/// <returns>函数返回值</returns>
public delegate object? DslFunction(object?[] args, IStateContainer state);

/// <summary>
/// 表达式 AST 求值器
/// <para>将 Pidgin 解析出的 AST 在给定状态容器下求值为运行时值。</para>
/// <para>支持：算术/比较/逻辑运算（含短路）、函数调用、三元条件、变量路径。</para>
/// </summary>
public static class ExpressionEvaluator
{
    // ====== 函数注册表 ======

    private static readonly ConcurrentDictionary<string, DslFunction> _functions = new();

    static ExpressionEvaluator()
    {
        // 内置函数
        RegisterFunction("random", (args, _) =>
        {
            var min = ConvertToInt(args[0]);
            var max = ConvertToInt(args[1]);
            return Random.Shared.Next(min, max + 1);
        });

        RegisterFunction("min", (args, _) =>
        {
            var l = ToDouble(args[0]);
            var r = ToDouble(args[1]);
            return Math.Min(l, r);
        });

        RegisterFunction("max", (args, _) =>
        {
            var l = ToDouble(args[0]);
            var r = ToDouble(args[1]);
            return Math.Max(l, r);
        });

        RegisterFunction("abs", (args, _) => Math.Abs(ToDouble(args[0])));

        RegisterFunction("clamp", (args, _) =>
        {
            var v = ToDouble(args[0]);
            var min = ToDouble(args[1]);
            var max = ToDouble(args[2]);
            return Math.Clamp(v, min, max);
        });
    }

    /// <summary>
    /// 注册自定义函数
    /// </summary>
    /// <param name="name">函数名（不区分大小写）</param>
    /// <param name="func">函数实现</param>
    public static void RegisterFunction(string name, DslFunction func)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(func);
        _functions[name.ToLowerInvariant()] = func;
    }

    /// <summary>
    /// 注销函数
    /// </summary>
    public static bool UnregisterFunction(string name)
        => _functions.TryRemove(name.ToLowerInvariant(), out _);

    // ====== 求值入口 ======

    /// <summary>
    /// 求值 AST——返回 object?（保留类型）
    /// </summary>
    public static object? Evaluate(Expr ast, IStateContainer state)
    {
        return ast switch
        {
            LiteralExpr lit => lit.Value,
            VariableExpr var => ResolveVariable(var.Path, state),
            BinaryExpr bin => EvaluateBinary(bin, state),
            UnaryExpr un => EvaluateUnary(un, state),
            FunctionCallExpr call => EvaluateFunctionCall(call, state),
            ConditionalExpr cond => ToBool(Evaluate(cond.Condition, state))
                ? Evaluate(cond.ThenExpr, state)
                : Evaluate(cond.ElseExpr, state),
            _ => null
        };
    }

    /// <summary>
    /// 求值为 bool——用于 if/elif 条件判断
    /// </summary>
    public static bool EvaluateBool(Expr ast, IStateContainer state)
        => ToBool(Evaluate(ast, state));

    /// <summary>
    /// 求值为 double——用于算术运算
    /// </summary>
    public static double EvaluateNumber(Expr ast, IStateContainer state)
        => ToDouble(Evaluate(ast, state));

    // ====== 二元运算（含短路逻辑）======

    private static object? EvaluateBinary(BinaryExpr bin, IStateContainer state)
    {
        // 短路逻辑：&& 和 ||
        if (bin.Op == "&&")
        {
            var left = ToBool(Evaluate(bin.Left, state));
            if (!left) return false;
            return ToBool(Evaluate(bin.Right, state));
        }

        if (bin.Op == "||")
        {
            var left = ToBool(Evaluate(bin.Left, state));
            if (left) return true;
            return ToBool(Evaluate(bin.Right, state));
        }

        // 非短路运算：先求值两侧
        var lVal = Evaluate(bin.Left, state);
        var rVal = Evaluate(bin.Right, state);

        return bin.Op switch
        {
            "+" => Add(lVal, rVal),
            "-" => ToDouble(lVal) - ToDouble(rVal),
            "*" => ToDouble(lVal) * ToDouble(rVal),
            "/" => ToDouble(rVal) != 0 ? ToDouble(lVal) / ToDouble(rVal) : 0,
            "%" => ToDouble(rVal) != 0 ? ToDouble(lVal) % ToDouble(rVal) : 0,
            "==" => Equals(lVal, rVal),
            "!=" => !Equals(lVal, rVal),
            ">" => ToDouble(lVal) > ToDouble(rVal),
            "<" => ToDouble(lVal) < ToDouble(rVal),
            ">=" => ToDouble(lVal) >= ToDouble(rVal),
            "<=" => ToDouble(lVal) <= ToDouble(rVal),
            _ => null
        };
    }

    // ====== 一元运算 ======

    private static object? EvaluateUnary(UnaryExpr un, IStateContainer state)
    {
        var val = Evaluate(un.Operand, state);
        return un.Op switch
        {
            "!" => !ToBool(val),
            "-" => -ToDouble(val),
            _ => val
        };
    }

    // ====== 函数调用 ======

    private static object? EvaluateFunctionCall(FunctionCallExpr call, IStateContainer state)
    {
        var name = call.FunctionName.ToLowerInvariant();
        if (!_functions.TryGetValue(name, out var func))
        {
            System.Diagnostics.Debug.WriteLine($"[ExpressionEvaluator] 未知函数: {call.FunctionName}");
            return null;
        }

        var args = new object?[call.Arguments.Count];
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            args[i] = Evaluate(call.Arguments[i], state);
        }

        try
        {
            return func(args, state);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExpressionEvaluator] 函数调用异常: {call.FunctionName} -> {ex.Message}");
            return null;
        }
    }

    // ====== 变量解析 ======

    /// <summary>
    /// 解析变量路径——支持特殊时间变量、扁平 key、嵌套字典路径
    /// <para>优先级：状态容器中的显式值 > 时间特殊变量 > null</para>
    /// </summary>
    private static object? ResolveVariable(string path, IStateContainer state)
    {
        // 单段路径：先检查状态容器中的显式值
        var parts = path.Split('.');
        if (parts.Length == 1)
        {
            var stateValue = state.Get<object>(path);
            if (stateValue != null)
                return stateValue;

            // 特殊变量：days / hours / mins / minutes（仅当状态中不存在时作为回退）
            if (path.Length <= 7)
            {
                var lower = path.ToLowerInvariant();
                if (lower is "days" or "day")
                    return (state.Get<long>(StateKeys.GameTime.TotalMinutes) / 1440).ToString();
                if (lower is "hours" or "hour")
                    return ((state.Get<long>(StateKeys.GameTime.TotalMinutes) % 1440) / 60).ToString();
                if (lower is "mins" or "min" or "minutes")
                    return (state.Get<long>(StateKeys.GameTime.TotalMinutes) % 60).ToString();
            }

            return stateValue;
        }

        // 多段路径：先尝试扁平 key
        var flatValue = state.Get<object>(path);
        if (flatValue != null)
            return flatValue;

        // 逐层递归：player → stats → hp
        object? current = state.Get<object>(parts[0]);
        if (current == null) return null;

        for (int i = 1; i < parts.Length; i++)
        {
            if (current is Dictionary<string, object?> dict)
            {
                dict.TryGetValue(parts[i], out current);
            }
            else if (current is Dictionary<string, object> dict2)
            {
                dict2.TryGetValue(parts[i], out var val);
                current = val;
            }
            else if (current is System.Collections.IDictionary idict)
            {
                current = idict.Contains(parts[i]) ? idict[parts[i]] : null;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    // ====== 类型转换辅助 ======

    private static double ToDouble(object? val) =>
        Convert.ToDouble(val ?? 0, CultureInfo.InvariantCulture);

    private static int ConvertToInt(object? val) =>
        Convert.ToInt32(val ?? 0, CultureInfo.InvariantCulture);

    private static bool ToBool(object? val)
    {
        if (val == null) return false;
        if (val is bool b) return b;
        if (val is int i) return i != 0;
        if (val is double d) return d != 0;
        if (val is long l) return l != 0;
        if (val is string s) return !string.IsNullOrEmpty(s);
        return true;
    }

    /// <summary>
    /// 加法——数字相加或字符串拼接
    /// </summary>
    private static object? Add(object? left, object? right)
    {
        // 字符串拼接
        if (left is string sl || right is string sr)
        {
            return (left?.ToString() ?? "") + (right?.ToString() ?? "");
        }

        // 数字相加
        var l = ToDouble(left);
        var r = ToDouble(right);
        var result = l + r;
        return result == (int)result ? (object)(int)result : result;
    }
}
