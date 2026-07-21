using System.Reflection;
using FluentAssertions;
using LingFanEngine.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// 58 个 handler 路由静态核对（盲区分析 T18）。
/// <para>核对：ICommandHandler&lt;&gt; 实现类数量 == 58 == DI 注册的 IDefaultCommandHandler 数量，
/// 实现类型无重复（无重复注册/死代码），且每个 handler 类都出现在 DI 注册中（无未注册）。</para>
/// </summary>
public class HandlerRoutingTests
{
    private static readonly System.Type s_cmdHandlerOpen =
        typeof(LingFanEngine.Abstractions.Interfaces.Core.ICommandHandler<>);
    private static readonly System.Type s_defaultHandler =
        typeof(LingFanEngine.Abstractions.Interfaces.Core.IDefaultCommandHandler);

    [Fact]
    public void HandlerClasses_Are58_AllDefault_AllRegistered_NoDuplicates()
    {
        var asm = typeof(LingFanEngine.Services.Core.Handlers.PlayVoiceHandler).Assembly;

        var handlerTypes = asm.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true }
                && t.GetInterfaces().Any(i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == s_cmdHandlerOpen))
            .ToArray();

        handlerTypes.Length.Should().Be(58, "引擎应有 58 个 ICommandHandler 实现");

        // 全部实现 IDefaultCommandHandler（统一策略路由的前提）
        handlerTypes.Should().OnlyContain(t =>
            System.Array.Exists(t.GetInterfaces(), i => i == s_defaultHandler));

        // DI 注册：AddLingFanEngine 应注册恰好 58 个 IDefaultCommandHandler，且实现类型不重复
        var sc = new ServiceCollection();
        sc.AddLingFanEngine();
        var registrations = sc
            .Where(d => d.ServiceType == s_defaultHandler)
            .ToList();

        registrations.Count.Should().Be(58, "DI 应注册 58 个 IDefaultCommandHandler");
        registrations.Select(d => d.ImplementationType).Distinct().Count().Should().Be(58, "不应有重复实现类型");

        // 每个 handler 类都应出现在 DI 注册中（无死代码 / 未注册）
        foreach (var ht in handlerTypes)
            registrations.Should().Contain(d => d.ImplementationType == ht);
    }
}
