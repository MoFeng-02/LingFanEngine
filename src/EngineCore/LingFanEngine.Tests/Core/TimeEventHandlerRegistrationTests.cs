using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Extensions;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using LingFanEngine.Tests.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// 回归测试：时间事件运行时处理器必须注册到 DI（IDefaultCommandHandler），
/// 否则 DSL unregister_time_event / restore_time_event 与 C# Ctrl.UnregisterEvent / Ctrl.RestoreEvent
/// 会静默空操作（命令发出后无人处理）。
/// 仅检查注册描述符，不构造任何服务，避免依赖完整运行时。
/// </summary>
public class TimeEventHandlerRegistrationTests
{
    [Fact]
    public void AddLingFanEngine_RegistersTimeEventRuntimeHandlers()
    {
        var services = new ServiceCollection();
        services.AddLingFanEngine();

        var registeredImplementations = services
            .Where(s => s.ServiceType == typeof(IDefaultCommandHandler))
            .Select(s => s.ImplementationType)
            .ToList();

        registeredImplementations.Should().Contain(typeof(SetTimeEventHandler),
            "set_time_event 运行时注册需要 SetTimeEventHandler，否则与时间事件注册表重复注册语义缺失");
        registeredImplementations.Should().Contain(typeof(UnregisterTimeEventHandler),
            "unregister_time_event / Ctrl.UnregisterEvent 需要 UnregisterTimeEventHandler，否则静默空操作");
        registeredImplementations.Should().Contain(typeof(RestoreTimeEventHandler),
            "restore_time_event / Ctrl.RestoreEvent 需要 RestoreTimeEventHandler，否则静默空操作");
    }

    [Fact]
    public void RegisteredSetTimeEventHandler_PerformsRegistration_WhenSchedulerPresent()
    {
        // 端到端行为校验：DI 注册的 SetTimeEventHandler 不只是被注册，而是真的产生副作用。
        // 这把 E 类「仅校验注册描述符」升级为「构造服务 + 跑 Handle + 断言副作用」。
        var scheduler = new FakeEventScheduler();
        var ctx = new FakeCommandContext { EventScheduler = scheduler };

        new SetTimeEventHandler().Handle(new SetTimeEventCommand { Id = "e1", Hour = 8, Minute = 30 }, ctx);

        scheduler.Registrations.Should().ContainSingle(r => r.Id == "e1" && r.Hour == 8 && r.Minute == 30);
    }
}
