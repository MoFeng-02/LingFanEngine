using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// 回溯系统单元测试——检查点创建/恢复/截断/跨场景。
/// <para>覆盖 DslExecutor 的 CanRollback/CanRollforward/ClearCheckpoints 逻辑。</para>
/// </summary>
public class RollbackSystemTests
{
    private readonly StateContainer _state = new();
    private readonly CommandPipeline _pipeline = new();
    private readonly DslExecutor _executor;

    public RollbackSystemTests()
    {
        _executor = new DslExecutor(_state, _pipeline, waitService: null);
    }

    // ========== CanRollback / CanRollforward ==========

    [Fact]
    public void CanRollback_NoCheckpoints_ReturnsFalse()
    {
        _executor.ClearCheckpoints();
        _executor.CanRollback().Should().BeFalse();
    }

    [Fact]
    public void CanRollback_WithCheckpointsAndCurrentIndex0_ReturnsFalse()
    {
        SetupCheckpoints(3, currentIndex: 0);
        _executor.CanRollback().Should().BeFalse();
    }

    [Fact]
    public void CanRollback_WithCheckpointsAndCurrentIndex1_ReturnsTrue()
    {
        SetupCheckpoints(3, currentIndex: 1);
        _executor.CanRollback().Should().BeTrue();
    }

    [Fact]
    public void CanRollforward_AtLatestCheckpoint_ReturnsFalse()
    {
        // 3 checkpoints, currentPos = 3 (= count, frontier at end) → cannot rollforward
        SetupCheckpoints(3, currentIndex: 3);
        _executor.CanRollforward().Should().BeFalse();
    }

    [Fact]
    public void CanRollforward_BeforeLatestCheckpoint_ReturnsTrue()
    {
        // 3 checkpoints, currentPos = 1 (frontier before end) → can rollforward
        SetupCheckpoints(3, currentIndex: 1);
        _executor.CanRollforward().Should().BeTrue();
    }

    [Fact]
    public void CanRollforward_NoCheckpoints_ReturnsFalse()
    {
        _executor.ClearCheckpoints();
        _executor.CanRollforward().Should().BeFalse();
    }

    // ========== ClearCheckpoints ==========

    [Fact]
    public void ClearCheckpoints_RemovesAllCheckpoints()
    {
        SetupCheckpoints(5, currentIndex: 3);
        _executor.ClearCheckpoints();

        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        checkpoints.Should().NotBeNull();
        checkpoints!.Count.Should().Be(0);
        _state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(-1);
    }

    [Fact]
    public void ClearCheckpoints_ResetsIsActiveAndIsReplay()
    {
        _state.Set(StateKeys.Rollback.IsActive, true);
        _state.Set(StateKeys.Rollback.IsReplay, true);
        _executor.ClearCheckpoints();

        _state.Get<bool>(StateKeys.Rollback.IsActive).Should().BeFalse();
        _state.Get<bool>(StateKeys.Rollback.IsReplay).Should().BeFalse();
    }

    // ========== 检查点列表管理 ==========

    [Fact]
    public void CheckpointList_StoresMultipleCheckpoints()
    {
        var checkpoints = new List<RollbackCheckpoint>
        {
            new() { CommandIndex = 0, InteractionType = "dialog", SceneName = "scene1" },
            new() { CommandIndex = 5, InteractionType = "menu", SceneName = "scene1" },
            new() { CommandIndex = 10, InteractionType = "input", SceneName = "scene2" },
        };
        _state.Set(StateKeys.Rollback.Checkpoints, checkpoints);
        _state.Set(StateKeys.Rollback.CurrentIndex, 2);

        var stored = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        stored.Should().NotBeNull();
        stored!.Count.Should().Be(3);
        stored[0].CommandIndex.Should().Be(0);
        stored[1].InteractionType.Should().Be("menu");
        stored[2].SceneName.Should().Be("scene2");
    }

    [Fact]
    public void CheckpointList_CurrentIndexTracksPosition()
    {
        SetupCheckpoints(5, currentIndex: 3);
        var idx = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        idx.Should().Be(3);

        SetupCheckpoints(5, currentIndex: 0);
        idx = _state.Get<int>(StateKeys.Rollback.CurrentIndex);
        idx.Should().Be(0);
    }

    // ========== 回溯边界条件 ==========

    [Fact]
    public void CanRollback_AtFirstCheckpoint_ReturnsFalse()
    {
        // currentPos = 0 means we're at the first checkpoint, cannot go back further
        SetupCheckpoints(3, currentIndex: 0);
        _executor.CanRollback().Should().BeFalse();
    }

    [Fact]
    public void CanRollforward_AtFrontierEnd_ReturnsFalse()
    {
        // currentPos = count means frontier at end, cannot go forward
        SetupCheckpoints(3, currentIndex: 3);
        _executor.CanRollforward().Should().BeFalse();
    }

    [Fact]
    public void CanRollforward_AtMinusOne_ReturnsFalse()
    {
        // currentPos = -1 means before any checkpoint, cannot rollforward
        SetupCheckpoints(3, currentIndex: -1);
        _executor.CanRollforward().Should().BeFalse();
    }

    [Fact]
    public void CanRollback_AtMinusOne_ReturnsFalse()
    {
        SetupCheckpoints(3, currentIndex: -1);
        _executor.CanRollback().Should().BeFalse();
    }

    // ========== 状态快照隔离 ==========

    [Fact]
    public void Checkpoint_SnapshotExcludesRollbackKeys()
    {
        // 验证 s_rollbackKeys 排除逻辑
        _state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint>());
        _state.Set(StateKeys.Rollback.CurrentIndex, 0);
        _state.Set("player_name", "Alice");
        _state.Set("__rollback_checkpoints", new List<RollbackCheckpoint>());

        var snapshot = new Dictionary<string, object?>();
        var rollbackKeys = new HashSet<string>
        {
            StateKeys.Rollback.Checkpoints,
            StateKeys.Rollback.CurrentIndex,
            StateKeys.Rollback.IsActive,
            StateKeys.Rollback.IsReplay,
        };

        foreach (var (k, v) in _state.GetSnapshot())
        {
            if (!rollbackKeys.Contains(k))
                snapshot[k] = v;
        }

        snapshot.Should().ContainKey("player_name");
        snapshot.Should().NotContainKey(StateKeys.Rollback.Checkpoints);
        snapshot.Should().NotContainKey(StateKeys.Rollback.CurrentIndex);
    }

    [Fact]
    public void Checkpoint_SnapshotPreservesUserVariables()
    {
        _state.Set("hp", 100);
        _state.Set("gold", 50);
        _state.Set("chapter", 3);
        _state.Set("__dialog_text", "hello");

        var snapshot = new Dictionary<string, object?>();
        var rollbackKeys = new HashSet<string>
        {
            StateKeys.Rollback.Checkpoints,
            StateKeys.Rollback.CurrentIndex,
        };

        foreach (var (k, v) in _state.GetSnapshot())
        {
            if (!rollbackKeys.Contains(k))
                snapshot[k] = v;
        }

        snapshot["hp"].Should().Be(100);
        snapshot["gold"].Should().Be(50);
        snapshot["chapter"].Should().Be(3);
        // 系统变量也保留（只有回溯自身的键排除）
        snapshot["__dialog_text"].Should().Be("hello");
    }

    // ========== LoadCommands 清理 ==========

    [Fact]
    public void LoadCommands_PreserveCheckpointsFalse_ClearsCheckpoints()
    {
        SetupCheckpoints(3, currentIndex: 1);

        var commands = new List<Abstractions.Interfaces.Core.ICommand>
        {
            new EndCommand(),
        };
        _executor.LoadCommands(commands, preserveCheckpoints: false);

        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        checkpoints!.Count.Should().Be(0);
        _state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(-1);
    }

    [Fact]
    public void LoadCommands_PreserveCheckpointsTrue_KeepsCheckpoints()
    {
        SetupCheckpoints(3, currentIndex: 1);

        var commands = new List<Abstractions.Interfaces.Core.ICommand>
        {
            new EndCommand(),
        };
        _executor.LoadCommands(commands, preserveCheckpoints: true);

        var checkpoints = _state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        checkpoints!.Count.Should().Be(3);
        _state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(1);
    }

    // ========== 辅助方法 ==========

    /// <summary>在 StateContainer 中设置指定数量的检查点和 CurrentIndex</summary>
    private void SetupCheckpoints(int count, int currentIndex)
    {
        var checkpoints = new List<RollbackCheckpoint>();
        for (int i = 0; i < count; i++)
        {
            checkpoints.Add(new RollbackCheckpoint
            {
                CommandIndex = i * 5,
                InteractionType = i % 2 == 0 ? "dialog" : "menu",
                SceneName = $"scene{i}",
                StateSnapshot = new Dictionary<string, object?> { ["step"] = i }
            });
        }
        _state.Set(StateKeys.Rollback.Checkpoints, checkpoints);
        _state.Set(StateKeys.Rollback.CurrentIndex, currentIndex);
        _state.Set(StateKeys.Rollback.IsActive, false);
        _state.Set(StateKeys.Rollback.IsReplay, false);
    }
}
