using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Saves;
using Xunit;

namespace LingFanEngine.Tests.Saves;

/// <summary>
/// SaveDataService 测试：BuildSaveData / ApplySaveData / SaveSystemState / LoadSystemStateAsync / OnSaveMigration。
/// <para>使用真实 StateContainer、JsonValueConverter、SceneStack 与 LingFanEngineOptions，ISaveService 用手工 Fake。</para>
/// </summary>
public class SaveDataServiceTests
{
    private static SaveDataService CreateService(
        IStateContainer state,
        LingFanEngineOptions? options = null,
        ISaveService? saveService = null)
    {
        return new SaveDataService(
            state,
            new JsonValueConverter(),
            options ?? new LingFanEngineOptions(),
            saveService,
            new SceneStack(state));
    }

    // ========== BuildSaveData（Game 场景）==========
    [Fact]
    public void BuildSaveData_GameScene_CollectsUserState_WithTypedSaveEntries()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        state.Set(StateKeys.Scene.CurrentName, "chapter1");
        state.Set("player_hp", 100);
        state.Set("player_name", "勇者");
        state.Set("alive", true);
        state.Set("ratio", 1.5);
        state.Set("big_id", 9000000000L);

        var data = CreateService(state).BuildSaveData();

        data.Should().NotBeNull();
        data!.SceneName.Should().Be("chapter1");
        data.TypedState.Should().NotBeNull();
        var ts = data.TypedState!;

        ts.Should().ContainKey("player_hp");
        ts["player_hp"].Type.Should().Be(SaveEntryTypes.Int);
        ts["player_hp"].Value.Should().Be(100);

        ts.Should().ContainKey("player_name");
        ts["player_name"].Type.Should().Be(SaveEntryTypes.String);
        ts["player_name"].Value.Should().Be("勇者");

        ts.Should().ContainKey("alive");
        ts["alive"].Type.Should().Be(SaveEntryTypes.Bool);
        ts["alive"].Value.Should().Be(true);

        ts.Should().ContainKey("ratio");
        ts["ratio"].Type.Should().Be(SaveEntryTypes.Double);
        ts["ratio"].Value.Should().Be(1.5);

        ts.Should().ContainKey("big_id");
        ts["big_id"].Type.Should().Be(SaveEntryTypes.Long);
        ts["big_id"].Value.Should().Be(9000000000L);
    }

    [Fact]
    public void BuildSaveData_GameScene_ExcludesSystemKeys()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        state.Set(StateKeys.Scene.CurrentName, "chapter1");
        state.Set("__some_system_flag", 42);
        state.Set("user_var", "value");

        var data = CreateService(state).BuildSaveData();

        data.Should().NotBeNull();
        data!.TypedState!.Should().ContainKey("user_var");
        data.TypedState.Should().NotContainKey("__some_system_flag");
        data.TypedState.Should().NotContainKey(StateKeys.Scene.CurrentName);
    }

    // ========== BuildSaveData 返回 null ==========
    [Fact]
    public void BuildSaveData_NonGameScene_NoMenuReturnTo_ReturnsNull()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Menu);
        state.Set(StateKeys.Scene.CurrentName, "title_main");

        var data = CreateService(state).BuildSaveData();

        data.Should().BeNull();
    }

    [Fact]
    public void BuildSaveData_UIScene_NoMenuReturnTo_ReturnsNull()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.UI);

        var data = CreateService(state).BuildSaveData();

        data.Should().BeNull();
    }

    // ========== ApplySaveData 往返 ==========
    [Fact]
    public void ApplySaveData_RestoresTypedState_RoundTrip()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        state.Set(StateKeys.Scene.CurrentName, "chapter1");
        state.Set("player_hp", 100);
        state.Set("player_name", "勇者");
        state.Set("alive", true);

        var svc = CreateService(state);
        var data = svc.BuildSaveData()!;

        // 清空用户变量，模拟"读档前状态"
        state.Remove("player_hp");
        state.Remove("player_name");
        state.Remove("alive");
        state.Get<int>("player_hp").Should().Be(0);

        svc.ApplySaveData(data);

        state.Get<int>("player_hp").Should().Be(100);
        state.Get<string>("player_name").Should().Be("勇者");
        state.Get<bool>("alive").Should().BeTrue();
        // 读档后场景类型被强制重置为 Game
        state.Get<int>(StateKeys.Scene.CurrentType).Should().Be((int)SceneType.Game);
        state.Get<string>(StateKeys.Scene.CurrentName).Should().Be("chapter1");
    }

    // ========== SaveSystemState + LoadSystemStateAsync ==========
    [Fact]
    public async Task SaveSystemState_Then_LoadSystemStateAsync_RestoresSystemKeys()
    {
        var state = new StateContainer();
        var fake = new FakeSaveService();
        var svc = CreateService(state, saveService: fake);

        state.Set("__test_sys_flag", 42);
        state.Set("__another_sys", "persisted");

        svc.SaveSystemState();

        fake.Saved.Should().ContainKey(StateKeys.SystemSaveSlot);

        // 清除系统键后从存档恢复
        state.Remove("__test_sys_flag");
        state.Remove("__another_sys");

        await svc.LoadSystemStateAsync();

        state.Get<int>("__test_sys_flag").Should().Be(42);
        state.Get<string>("__another_sys").Should().Be("persisted");
    }

    [Fact]
    public async Task SaveSystemState_WithoutSaveService_DoesNothing()
    {
        var state = new StateContainer();
        // 不传入 ISaveService
        var svc = CreateService(state);
        state.Set("__test_sys_flag", 7);

        svc.SaveSystemState(); // 不应抛异常
        await svc.LoadSystemStateAsync(); // 不应抛异常
        state.Get<int>("__test_sys_flag").Should().Be(7);
    }

    // ========== OnSaveMigration 版本迁移回调 ==========
    [Fact]
    public void ApplySaveData_OnSaveMigration_RejectingLoad_SkipsApply()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        var svc = CreateService(state);

        var data = new SaveData
        {
            GameVersion = "0.0.1",
            SceneName = "chapter1",
            TypedState = new Dictionary<string, SaveEntry>
            {
                ["hp"] = new() { Type = SaveEntryTypes.Int, Value = 999 }
            }
        };

        bool invoked = false;
        svc.OnSaveMigration = (d, current) =>
        {
            invoked = true;
            return false; // 拒绝加载
        };

        state.Set("marker", 1);
        svc.ApplySaveData(data);

        invoked.Should().BeTrue();
        state.ContainsKey("hp").Should().BeFalse(); // 未应用
        state.Get<int>("marker").Should().Be(1);    // 状态未被清空
    }

    [Fact]
    public void ApplySaveData_OnSaveMigration_AcceptingLoad_Applies()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        var svc = CreateService(state);

        var data = new SaveData
        {
            GameVersion = "0.0.1",
            SceneName = "chapter1",
            TypedState = new Dictionary<string, SaveEntry>
            {
                ["hp"] = new() { Type = SaveEntryTypes.Int, Value = 888 }
            }
        };

        svc.OnSaveMigration = (d, current) => true; // 接受加载
        svc.ApplySaveData(data);

        state.Get<int>("hp").Should().Be(888);
    }
}

/// <summary>
/// 内存版 ISaveService Fake：按 slotId 保存/读取 SaveData。
/// </summary>
internal sealed class FakeSaveService : ISaveService
{
    public Dictionary<string, SaveData> Saved { get; } = new();

    public Task<SaveData?> LoadAsync(string slotId)
        => Task.FromResult(Saved.TryGetValue(slotId, out var d) ? d : null);

    public Task SaveAsync(string slotId, SaveData data)
    {
        Saved[slotId] = data;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string slotId)
    {
        Saved.Remove(slotId);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<SaveSlotInfo>> GetAllSaveSlotsAsync()
        => Task.FromResult(Enumerable.Empty<SaveSlotInfo>());

    public Task<bool> ExistsAsync(string slotId)
        => Task.FromResult(Saved.ContainsKey(slotId));
}
