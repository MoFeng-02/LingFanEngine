using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// SaveLoadHandler 测试：断言存档命令调用 ISaveService.SaveAsync，读档命令调用 ApplySaveData。
/// <para>读档路径经由 Task.Run 异步执行，测试等待 ApplySaveData 回调完成。</para>
/// </summary>
public class SaveLoadHandlerTests
{
    private static SaveData MakeSaveData() => new() { GameVersion = "1.0.0", SceneName = "town" };

    [Fact]
    public void Handle_Save_WithNoSaveService_DoesNothing()
    {
        var ctx = new FakeCommandContext { SaveService = null };
        var handler = new SaveLoadHandler();
        var cmd = new SaveLoadCommand { SlotId = "slot1", IsSave = true };

        // 不应抛异常
        var act = () => handler.Handle(cmd, ctx);
        act.Should().NotThrow();
    }

    [Fact]
    public void Handle_Save_WithNoBuildableSaveData_SkipsSave()
    {
        var save = new FakeSaveService();
        var ctx = new FakeCommandContext { SaveService = save, SaveDataProvider = () => null };
        var handler = new SaveLoadHandler();
        var cmd = new SaveLoadCommand { SlotId = "slot1", IsSave = true };

        handler.Handle(cmd, ctx);

        // BuildSaveData 返回 null → 不调用 SaveAsync
        save.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Save_InvokesSaveServiceWithSlot()
    {
        var save = new FakeSaveService();
        var ctx = new FakeCommandContext { SaveService = save, SaveDataProvider = MakeSaveData };
        var handler = new SaveLoadHandler();
        var cmd = new SaveLoadCommand { SlotId = "my_slot", IsSave = true };

        handler.Handle(cmd, ctx);

        // SaveAsync 同步完成，Handle 返回时必然已调用
        save.SaveCount.Should().Be(1);
        save.LastSavedSlot.Should().Be("my_slot");
        save.LastSavedData.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_Load_InvokesApplySaveData()
    {
        var save = new FakeSaveService { DataToLoad = MakeSaveData() };
        var tcs = new TaskCompletionSource<SaveData>();
        var ctx = new FakeCommandContext
        {
            SaveService = save,
            ApplySaveDataAction = d => tcs.TrySetResult(d)
        };
        var handler = new SaveLoadHandler();
        var cmd = new SaveLoadCommand { SlotId = "slot9", IsSave = false };

        handler.Handle(cmd, ctx);

        // 等待 Task.Run 内的异步读档 + ApplySaveData 完成（带超时保护）
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        completed.Should().Be(tcs.Task, "读档应回调 ApplySaveData");
        save.LoadCount.Should().Be(1);
        save.LastLoadedSlot.Should().Be("slot9");
        (await tcs.Task).SceneName.Should().Be("town");
    }

    [Fact]
    public async Task Handle_Load_MissingSlot_DoesNotApplySave()
    {
        var save = new FakeSaveService { DataToLoad = null };
        var applied = false;
        var ctx = new FakeCommandContext
        {
            SaveService = save,
            ApplySaveDataAction = _ => applied = true
        };
        var handler = new SaveLoadHandler();
        var cmd = new SaveLoadCommand { SlotId = "missing", IsSave = false };

        handler.Handle(cmd, ctx);

        await Task.Delay(300);
        applied.Should().BeFalse();
        save.LoadCount.Should().Be(1);
    }
}
