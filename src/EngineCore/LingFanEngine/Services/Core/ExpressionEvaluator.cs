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
        // random：确定性 RNG（seed+counter 经 SplitMix64）——回溯重放一致（见 NextDeterministic 注释）
        RegisterFunction("random", (args, state) =>
        {
            var min = ConvertToInt(args[0]);
            var max = ConvertToInt(args[1]);
            return NextDeterministic(state, min, max);
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

    /// <summary>
    /// 表达式诊断回调（可选）——由宿主注入（如接 EngineLogger），报告除零等语义问题。
    /// <para>null（默认）时零开销；DSL 求值层的静默降级（如 x/0 返回 0）通过此钩子可观测。</para>
    /// </summary>
    public static Action<string>? OnDiagnostic { get; set; }

    /// <summary>
    /// 确定性随机：seed + counter 经 SplitMix64 派生 [min, max] 闭区间整数。
    /// <para>修复回溯不确定性：原先用全局 Random.Shared，回溯后重放含 random() 的分支条件
    /// 会得到不同随机数 → 同一检查点分支走向漂移。现在 counter 写入状态容器，
    /// 随回溯检查点快照保存/恢复 → 重放得到同一随机数序列。</para>
    /// <para>seed 在会话内首次调用时生成一次（真随机），保证不同会话不重复。</para>
    /// </summary>
    public static int NextDeterministic(IStateContainer state, int min, int max)
    {
        if (max < min) (min, max) = (max, min);

        var seed = state.Get<long>(StateKeys.Rng.Seed);
        if (seed == 0)
        {
            // 首次调用：生成真随机种子（Span<byte> 零额外分配，AOT 安全）
            Span<byte> buf = stackalloc byte[8];
            System.Security.Cryptography.RandomNumberGenerator.Fill(buf);
            seed = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(buf);
            if (seed == 0) seed = 1;
            state.Set(StateKeys.Rng.Seed, seed);
        }

        var counter = state.Get<long>(StateKeys.Rng.Counter) + 1;
        state.Set(StateKeys.Rng.Counter, counter);

        var z = (ulong)seed ^ ((ulong)counter * 0x9E3779B97F4A7C15UL);
        // SplitMix64
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;

        var range = (ulong)(max - min + 1L);
        return range == 0 ? min : (int)(min + (long)(z % range));
    }

    /// <summary>
    /// 尝试解析时间特殊变量（days/hours/mins 及单数形式）。
    /// <para>返回数值（long）而非字符串——修复 "hours == 12" 永假（字符串 vs 数值走 Equals 必不等）
    /// 而 "hours > 12" 却可用（ToDouble 隐式转换）的不一致行为。</para>
    /// <para>供 ExpressionEvaluator（AST 路径）与 ExpressionParser（模板回退路径）共用，保持两路径语义一致。</para>
    /// </summary>
    /// <param name="name">变量名（不区分大小写）</param>
    /// <param name="value">解析出的数值</param>
    /// <returns>是否为时间特殊变量</returns>
    public static bool TryGetTimeVariable(string name, IStateContainer state, out object? value)
    {
        value = null;
        if (name.Length > 7) return false;
        var lower = name.ToLowerInvariant();
        var total = state.Get<long>(StateKeys.GameTime.TotalMinutes);
        switch (lower)
        {
            case "days" or "day":
                value = total / 1440;
                return true;
            case "hours" or "hour":
                value = total % 1440 / 60;
                return true;
            case "mins" or "min" or "minutes":
                value = total % 60;
                return true;
            default:
                return false;
        }
    }

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
            "/" => Divide(ToDouble(lVal), ToDouble(rVal)),
            "%" => Modulo(ToDouble(lVal), ToDouble(rVal)),
            "==" => AreEqual(lVal, rVal),
            "!=" => !AreEqual(lVal, rVal),
            ">" => ToDouble(lVal) > ToDouble(rVal),
            "<" => ToDouble(lVal) < ToDouble(rVal),
            ">=" => ToDouble(lVal) >= ToDouble(rVal),
            "<=" => ToDouble(lVal) <= ToDouble(rVal),
            _ => null
        };
    }

    // ====== 一元运算 ======

    /// <summary>除法——除零静默返回 0（兼容既有行为），但经 OnDiagnostic 报告使其可观测</summary>
    private static double Divide(double l, double r)
    {
        if (r == 0)
        {
            OnDiagnostic?.Invoke($"除数为零: {l} / 0 → 返回 0");
            return 0;
        }
        return l / r;
    }

    /// <summary>取模——模零静默返回 0（兼容既有行为），但经 OnDiagnostic 报告使其可观测</summary>
    private static double Modulo(double l, double r)
    {
        if (r == 0)
        {
            OnDiagnostic?.Invoke($"模数为零: {l} % 0 → 返回 0");
            return 0;
        }
        return l % r;
    }

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
    /// <summary>
    /// 读取变量值——局部变量（let/local 写入 _local_&lt;name&gt;）优先遮蔽全局变量（set/define 写入 &lt;name&gt;）。
    /// <para>写路径约定见 LingFanDslEngine：let/local 的键为 "_local_" + key.Replace('.','_')，set/define 的键为字面 key。</para>
    /// <para>读路径与写路径严格一致，故 let/local 声明的变量正确可读，且与全局同名变量各自独立、互不污染。</para>
    /// </summary>
    private static object? ReadVariable(IStateContainer state, string name)
    {
        return LocalScope.Read(state, name);
    }

    private static object? ResolveVariable(string path, IStateContainer state)
    {
        // 单段路径：局部(_local_)优先遮蔽全局，与写路径 let/local -> _local_<name> 一致
        var direct = ReadVariable(state, path);
        if (direct != null) return direct;

        var parts = path.Split('.');
        if (parts.Length == 1)
        {
            // 特殊时间变量：days / hours / mins（仅当状态中不存在时作为回退）
            if (TryGetTimeVariable(path, state, out var timeValue))
                return timeValue;

            return null;
        }

        // 多段路径：扁平 key（含局部 _local_ 前缀）已在 ReadVariable 尝试；这里走嵌套字典 player -> stats -> hp
        object? current = ReadVariable(state, parts[0]);
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
        Convert.ToDouble(UnwrapJson(val) ?? 0, CultureInfo.InvariantCulture);

    /// <summary>
    /// 将存档/嵌套字典反序列化残留的 JsonElement 还原为 .NET 原生类型，
    /// 使状态值（常含 JsonElement）参与比较/算术时与字面量一致（B7 配套）。
    /// </summary>
    private static object? UnwrapJson(object? v)
    {
        if (v is not System.Text.Json.JsonElement je) return v;
        switch (je.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Number:
                if (je.TryGetInt32(out var i)) return i;
                if (je.TryGetInt64(out var l)) return l;
                return je.GetDouble();
            case System.Text.Json.JsonValueKind.String: return je.GetString();
            case System.Text.Json.JsonValueKind.True: return true;
            case System.Text.Json.JsonValueKind.False: return false;
            case System.Text.Json.JsonValueKind.Null: return null;
            default: return je;
        }
    }

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
    /// 相等比较——数值类型跨类型归一（如 int 与 double 比较，及 JsonElement 数值），其余类型走 object.Equals 以保留字符串/布尔/自定义类型的相等语义。
    /// <para>修复：原先直接 object.Equals 导致 <c>0 == 0.0</c>（int 装箱 vs double 装箱）及状态值（JsonElement）与字面量比较时误判不等（B7）。</para>
    /// </summary>
    private static bool AreEqual(object? l, object? r)
    {
        l = UnwrapJson(l);
        r = UnwrapJson(r);
        if (ReferenceEquals(l, r)) return true;   // 同引用或同为 null
        if (l is null || r is null) return false; // 仅一方为 null
        if (IsNumeric(l) && IsNumeric(r))
            return ToDouble(l) == ToDouble(r);     // 跨数值类型按 double 归一比较
        return Equals(l, r);                        // 字符串/布尔/同类型值走默认语义
    }

    /// <summary>
    /// 判断值是否为数值类型（用于 ==/!= 跨类型归一）
    /// </summary>
    private static bool IsNumeric(object? v) =>
        v is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

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
        // 仅当可无损表示为 int 时才收窄（整数且在范围内）：超出范围强转会得到错误的环绕值
        return result == Math.Floor(result) && result >= int.MinValue && result <= int.MaxValue
            ? (object)(int)result
            : result;
    }
}
