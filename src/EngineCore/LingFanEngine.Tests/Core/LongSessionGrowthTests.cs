using System.Collections;
using System.Reflection;
using FluentAssertions;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// 长会话资源增长测试（盲区验证 T14）。
/// <para>关注三类随会话增长的状态：</para>
/// <para>1) _templateAstCache（表达式 AST 缓存）——确认其随不同模板增长、按常量模板键复用（不因变量值膨胀）；</para>
/// <para>2) 缓存无驱逐机制（无界）——但键是 DSL 模板字符串（内容常量），实践中受内容量上界，风险低；</para>
/// <para>3) 状态容器 / 事件调度集合的增长由各自上限（MaxRollbackCheckpoints、事件终态集合）约束，不在此重复测。</para>
/// </summary>
public class LongSessionGrowthTests
{
    private static IDictionary GetCache()
    {
        var field = typeof(ExpressionParser).GetField("_templateAstCache",
            BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        return (IDictionary)field!.GetValue(null)!;
    }

    [Fact]
    public void TemplateAstCache_GrowsWithDistinctTemplates_NeverEvicted()
    {
        var cache = GetCache();
        var before = cache.Count;

        // 100 个不同模板表达式（模拟长会话里不断出现的不同插值模板）
        for (int i = 0; i < 100; i++)
            ExpressionParser.Replace($"{{v{i}}}", new StateContainer());

        cache.Count.Should().Be(before + 100);

        // 重复相同模板 → 命中缓存，不新增
        for (int i = 0; i < 100; i++)
            ExpressionParser.Replace($"{{v{i}}}", new StateContainer());

        cache.Count.Should().Be(before + 100);
    }

    [Fact]
    public void TemplateAstCache_KeyedByConstantTemplate_NotByResolvedValue()
    {
        // 同一模板在不同变量值下应复用同一缓存项（不因值变化膨胀）
        var state = new StateContainer();
        state.Set("gold", 1);
        ExpressionParser.Replace("{gold}", state);
        state.Set("gold", 999999);
        ExpressionParser.Replace("{gold}", state);

        var cache = GetCache();
        cache.Contains("gold").Should().BeTrue();
        cache.Count.Should().BeGreaterThanOrEqualTo(1);
    }
}
