using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 表达式解析器——模块化的模板变量替换
/// <para>支持：
/// - {player.gold} → 扁平 key 或嵌套字典路径
/// - {player.stats.hp} → 嵌套字典深度路径
/// - {hours:00} → 数值格式化
/// - {days} → 特殊变量（从 IGameTimeService 计算）
/// - {gold + 50} → 算术表达式（Pidgin 引擎）
/// - {hp >= 100 && status == "alive"} → 逻辑表达式（Pidgin 引擎）
/// </para>
/// </summary>
public static class ExpressionParser
{
    private static readonly Regex _expressionPattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// 模板表达式 AST 缓存
    /// </summary>
    private static readonly ConcurrentDictionary<string, Expr?> _templateAstCache = new();

    /// <summary>
    /// 替换文本中的所有 {变量} 表达式
    /// </summary>
    /// <param name="text">模板文本</param>
    /// <param name="state">状态容器</param>
    /// <returns>替换后的文本</returns>
    public static string Replace(string text, IStateContainer state)
    {
        if (!text.Contains('{')) return text;

        return _expressionPattern.Replace(text, match =>
        {
            var expr = match.Groups[1].Value.Trim();

            // 支持格式化后缀：{mins:00} → var=mins, format=00
            // 如果表达式中包含 ?（三元条件），则不拆分冒号
            string? format = null;
            if (!expr.Contains('?'))
            {
                var colonIdx = expr.IndexOf(':');
                if (colonIdx > 0)
                {
                    format = expr[(colonIdx + 1)..].Trim();
                    expr = expr[..colonIdx].Trim();
                }
            }

            // 使用 Pidgin 引擎求值
            var rawValue = EvaluateExpression(expr, state);
            var strValue = rawValue?.ToString() ?? expr;

            // 应用格式化
            if (format != null)
            {
                strValue = ApplyFormat(strValue, format);
            }

            return strValue;
        });
    }

    /// <summary>
    /// 使用 Pidgin 引擎求值表达式（带缓存），失败时回退到旧引擎
    /// </summary>
    private static object? EvaluateExpression(string expr, IStateContainer state)
    {
        var ast = _templateAstCache.GetOrAdd(expr, e =>
        {
            var result = DslExpressionParser.Parse(e);
            return result.Success ? result.Value : null;
        });

        if (ast != null)
            return ExpressionEvaluator.Evaluate(ast, state);

        // Pidgin 解析失败，回退到旧引擎
        return ResolveValue(expr, state);
    }

    /// <summary>
    /// 解析表达式路径，从状态容器中取值
    /// <para>支持：
    /// - "player.gold" → 扁平 key
    /// - "player.stats.hp" → 嵌套字典路径（player.stats 是字典，取 hp）
    /// - "days" / "hours" / "mins" → 特殊计算变量
    /// </para>
    /// </summary>
    private static object? ResolveValue(string expr, IStateContainer state)
    {
        // 特殊变量：days / hours / mins / minutes
        if (expr.Length <= 7)
        {
            var lower = expr.ToLowerInvariant();
            if (lower is "days" or "day")
                return (state.Get<long>(StateKeys.GameTime.TotalMinutes) / 1440).ToString();
            if (lower is "hours" or "hour")
                return ((state.Get<long>(StateKeys.GameTime.TotalMinutes) % 1440) / 60).ToString();
            if (lower is "mins" or "min" or "minutes")
                return (state.Get<long>(StateKeys.GameTime.TotalMinutes) % 60).ToString();
        }

        // 从状态容器获取值
        var parts = expr.Split('.');
        if (parts.Length == 1)
        {
            // 扁平 key
            return state.Get<object>(expr);
        }

        // 多段路径：player.stats.hp
        // 先尝试扁平 key（player.stats.hp）
        var flatValue = state.Get<object>(expr);
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

    /// <summary>
    /// 应用格式化字符串
    /// <para>支持：:00 → D2，:000 → D3，:X → 十六进制等</para>
    /// </summary>
    private static string ApplyFormat(string value, string format)
    {
        if (!long.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
            return value;

        // 常见的 PadLeft 简化
        if (format.All(c => c == '0'))
        {
            var digits = format.Length;
            return num.ToString($"D{digits}");
        }

        try
        {
            return num.ToString(format);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExpressionParser] 格式化失败: {ex.Message}");
            return value;
        }
    }
}
