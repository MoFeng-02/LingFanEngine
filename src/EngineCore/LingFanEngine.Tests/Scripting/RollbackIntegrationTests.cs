using System.Collections.Generic;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>
/// 叙事模式逐句回溯集成测试（盲区验证 T13）。
/// <para>验证统一线性回溯时间线的核心机制：</para>
/// <para>1) Rollback() 真实恢复检查点快照（非仅移动索引）——直接驱动真实 DslExecutor + 真实 StateContainer；</para>
/// <para>2) 无检查点时 Rollback() 返回 false；</para>
/// <para>3) 小说世界模式(EnableTimeSystem)禁用逐句回溯——检查点不被创建；</para>
/// <para>4) MaxRollbackCheckpoints 超出时驱逐最旧检查点（防内存无限增长）。</para>
/// </summary>
public class RollbackIntegrationTests
{
    private static DslExecutor MakeExecutor(StateContainer state, bool enableTimeSystem = false)
    {
        var options = new LingFanEngineOptions { EnableTimeSystem = enableTimeSystem };
        return new DslExecutor(state, new CommandPipeline(), options, new AsyncWaitService(state));
    }

    [Fact]
    public void Rollback_RestoresPriorCheckpointState()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        state.Set("hp", 100);
        var exe = MakeExecutor(state);

        // 模拟两个检查点：cp0(hp=100) → cp1(hp=50)
        var cp0 = new RollbackCheckpoint
        {
            CommandIndex = 0,
            InteractionType = "dialog",
            StateSnapshot = new Dictionary<string, object?> { ["hp"] = 100 }
        };
        var cp1 = new RollbackCheckpoint
        {
            CommandIndex = 1,
            InteractionType = "dialog",
            StateSnapshot = new Dictionary<string, object?> { ["hp"] = 50 }
        };
        state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint> { cp0, cp1 });
        state.Set(StateKeys.Rollback.CurrentIndex, 1);

        // 当前运行时状态已被推进到 50（cp1 之后的修改）
        state.Set("hp", 50);

        exe.Rollback().Should().BeTrue();

        // 关键断言：状态被真实回滚到 cp0 的快照值
        state.Get<int>("hp").Should().Be(100);
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(0);
        // 回滚后处于 replay/rollback 会话中（cp1 仍在当前位置之前未重放），IsActive 应为 True
        state.Get<bool>(StateKeys.Rollback.IsActive).Should().BeTrue();
    }

    [Fact]
    public void Rollback_NoCheckpoint_ReturnsFalse()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        var exe = MakeExecutor(state);

        exe.Rollback().Should().BeFalse();
    }

    [Fact]
    public async Task WorldMode_DisablesCheckpointCreation()
    {
        var host = new EngineTestHost(enableTimeSystem: true);
        host.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        host.State.Set(StateKeys.Rollback.BlockedUntil, -1);

        var cmds = new LingFanDslEngine().Compile("""say "第一句" """).Commands;
        await host.RunDslAndDriveAsync(cmds);

        var cps = host.State.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        (cps == null || cps.Count == 0).Should().BeTrue();
    }

    [Fact]
    public async Task MaxRollbackCheckpoints_EvictsOldest()
    {
        var host = new EngineTestHost(enableTimeSystem: false);
        host.Options.MaxRollbackCheckpoints = 3;
        host.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        // 生产环境由 StateInitializer 将 BlockedUntil 初始化为 -1；EngineTestHost 不含该步骤，
        // 不设置则未设整型默认 0 会令 CreateCheckpoint 的 blockedUntil>=0 分支恒成立而跳过检查点。
        host.State.Set(StateKeys.Rollback.BlockedUntil, -1);

        var script = string.Join("\n", System.Linq.Enumerable.Range(1, 5).Select(i => $"say \"句{i}\""));
        var cmds = new LingFanDslEngine().Compile(script).Commands;
        await host.RunDslAndDriveAsync(cmds);

        var cps = host.State.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        cps.Should().NotBeNull();
        cps!.Count.Should().Be(3); // 超出上限，最旧被驱逐
    }
}
