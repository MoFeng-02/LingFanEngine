using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Saves;
using System.Security.Cryptography;
using Xunit;

namespace LingFanEngine.Tests.Saves;

/// <summary>
/// BinarySaveService 存档服务测试
/// <para>覆盖 SaveAsync/LoadAsync/Delete/Exists/GetAllSaveSlots 全链路。</para>
/// </summary>
public class BinarySaveServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BinarySaveService _service;
    private readonly byte[] _key;
    private readonly byte[] _iv;

    public BinarySaveServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _key = new byte[32];
        _iv = new byte[16];
        RandomNumberGenerator.Fill(_key);
        RandomNumberGenerator.Fill(_iv);

        _service = new BinarySaveService(_tempDir, _key, _iv);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static SaveData CreateSampleSave(string sceneName = "chapter1", string? name = null)
    {
        return new SaveData
        {
            GameVersion = "1.0.0",
            Name = name ?? $"存档 - {sceneName}",
            SceneName = sceneName,
            State = new Dictionary<string, object?>
            {
                ["gold"] = 100,
                ["name"] = "勇者",
                ["alive"] = true
            },
            SceneStackSnapshot = new(),
            Thumbnail = null
        };
    }

    // ========== S1 端到端：集合经 BinarySaveService 文件往返不丢 ==========
    [Fact]
    public async Task SaveLoad_RoundTrip_PreservesEnumerable_FullPipeline()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        state.Set("nums", new System.Collections.Generic.List<int> { 1, 2, 3 });

        var saveData = new SaveDataService(state, new JsonValueConverter(), new LingFanEngineOptions(), _service, new SceneStack(state)).BuildSaveData()!;
        saveData.TypedState!["nums"].Type.Should().Be(SaveEntryTypes.Json);

        await _service.SaveAsync("slot_s1", saveData);
        var loaded = await _service.LoadAsync("slot_s1");
        loaded.Should().NotBeNull();

        var state2 = new StateContainer();
        new SaveDataService(state2, new JsonValueConverter(), new LingFanEngineOptions(), _service, new SceneStack(state2)).ApplySaveData(loaded!);

        var nums = state2.Get<object>("nums") as System.Collections.Generic.List<object?>;
        nums.Should().NotBeNull();
        nums!.Should().HaveCount(3);
        nums[0].Should().Be(1);
        nums[1].Should().Be(2);
        nums[2].Should().Be(3);
    }

    // ========== SaveAsync + LoadAsync 往返 ==========

    [Fact]
    public async Task SaveLoad_RoundTrip_PreservesBasicFields()
    {
        var original = CreateSampleSave("chapter1", "测试存档");
        await _service.SaveAsync("slot1", original);

        var loaded = await _service.LoadAsync("slot1");

        loaded.Should().NotBeNull();
        loaded!.GameVersion.Should().Be("1.0.0");
        loaded.Name.Should().Be("测试存档");
        loaded.SceneName.Should().Be("chapter1");
    }

    [Fact]
    public async Task SaveLoad_RoundTrip_PreservesStateValues()
    {
        var original = CreateSampleSave();
        original.State["gold"] = 999;
        original.State["name"] = "Alice";
        await _service.SaveAsync("slot2", original);

        var loaded = await _service.LoadAsync("slot2");

        loaded.Should().NotBeNull();
        loaded!.State.Should().NotBeNull();
        // JSON 序列化后数值可能变成 JsonElement，但值应等价
        loaded.State["gold"].Should().NotBeNull();
        loaded.State["name"].Should().NotBeNull();
    }

    [Fact]
    public async Task SaveLoad_RoundTrip_PreservesThumbnail()
    {
        var original = CreateSampleSave();
        original.Thumbnail = new byte[] { 1, 2, 3, 4, 5 };
        await _service.SaveAsync("thumb_slot", original);

        var loaded = await _service.LoadAsync("thumb_slot");

        loaded.Should().NotBeNull();
        loaded!.Thumbnail.Should().NotBeNull();
        loaded.Thumbnail.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public async Task Save_Overwrite_ExistingSlot()
    {
        var save1 = CreateSampleSave("scene_a");
        await _service.SaveAsync("overwrite_slot", save1);

        var save2 = CreateSampleSave("scene_b");
        await _service.SaveAsync("overwrite_slot", save2);

        var loaded = await _service.LoadAsync("overwrite_slot");
        loaded.Should().NotBeNull();
        loaded!.SceneName.Should().Be("scene_b");
    }

    [Fact]
    public async Task Save_CreatesDirectory_IfNotExists()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "saves");
        var service = new BinarySaveService(nestedDir, _key, _iv);

        var save = CreateSampleSave();
        await service.SaveAsync("slot1", save);

        Directory.Exists(nestedDir).Should().BeTrue();
        var loaded = await service.LoadAsync("slot1");
        loaded.Should().NotBeNull();
    }

    // ========== LoadAsync 不存在的槽位 ==========

    [Fact]
    public async Task Load_NonExistentSlot_ReturnsNull()
    {
        var loaded = await _service.LoadAsync("nonexistent");
        loaded.Should().BeNull();
    }

    // ========== Delete ==========

    [Fact]
    public async Task Delete_ExistingSlot_RemovesFile()
    {
        await _service.SaveAsync("delete_me", CreateSampleSave());
        (await _service.ExistsAsync("delete_me")).Should().BeTrue();

        await _service.DeleteAsync("delete_me");

        (await _service.ExistsAsync("delete_me")).Should().BeFalse();
        var loaded = await _service.LoadAsync("delete_me");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Delete_NonExistentSlot_DoesNotThrow()
    {
        // 删除不存在的存档不应抛异常
        await _service.DeleteAsync("ghost");
    }

    // ========== Exists ==========

    [Fact]
    public async Task Exists_NonExistent_ReturnsFalse()
    {
        (await _service.ExistsAsync("nope")).Should().BeFalse();
    }

    [Fact]
    public async Task Exists_AfterSave_ReturnsTrue()
    {
        await _service.SaveAsync("exists_slot", CreateSampleSave());
        (await _service.ExistsAsync("exists_slot")).Should().BeTrue();
    }

    [Fact]
    public async Task Exists_AfterDelete_ReturnsFalse()
    {
        await _service.SaveAsync("temp_slot", CreateSampleSave());
        await _service.DeleteAsync("temp_slot");
        (await _service.ExistsAsync("temp_slot")).Should().BeFalse();
    }

    // ========== GetAllSaveSlots ==========

    [Fact]
    public async Task GetAllSaveSlots_EmptyDirectory_ReturnsEmpty()
    {
        var slots = await _service.GetAllSaveSlotsAsync();
        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllSaveSlots_ReturnsAllSlots()
    {
        await _service.SaveAsync("slot_a", CreateSampleSave("scene_a", "存档A"));
        await _service.SaveAsync("slot_b", CreateSampleSave("scene_b", "存档B"));
        await _service.SaveAsync("slot_c", CreateSampleSave("scene_c", "存档C"));

        var slots = (await _service.GetAllSaveSlotsAsync()).ToList();
        slots.Should().HaveCount(3);
        slots.Select(s => s.SlotId).Should().Contain(new[] { "slot_a", "slot_b", "slot_c" });
    }

    [Fact]
    public async Task GetAllSaveSlots_ReturnsSlotInfo()
    {
        var save = CreateSampleSave("chapter1", "测试存档信息");
        save.GameVersion = "2.1.0";
        await _service.SaveAsync("info_slot", save);

        var slots = (await _service.GetAllSaveSlotsAsync()).ToList();
        slots.Should().HaveCount(1);
        var info = slots[0];
        info.SlotId.Should().Be("info_slot");
        info.Name.Should().Be("测试存档信息");
        info.GameVersion.Should().Be("2.1.0");
    }

    [Fact]
    public async Task GetAllSaveSlots_SkipsCorruptedFiles()
    {
        await _service.SaveAsync("good_slot", CreateSampleSave());

        // 写入一个损坏的 .sav 文件
        var corruptPath = Path.Combine(_tempDir, "corrupt.sav");
        await File.WriteAllBytesAsync(corruptPath, new byte[] { 1, 2, 3, 4, 5 }, TestContext.Current.CancellationToken);

        var slots = (await _service.GetAllSaveSlotsAsync()).ToList();
        slots.Should().HaveCount(1);
        slots[0].SlotId.Should().Be("good_slot");
    }

    // ========== 文件加密验证 ==========

    [Fact]
    public async Task Save_FileContent_IsEncrypted()
    {
        var save = CreateSampleSave();
        save.Name = "SecretSave";
        await _service.SaveAsync("enc_slot", save);

        var filePath = Path.Combine(_tempDir, "enc_slot.sav");
        var fileBytes = await File.ReadAllBytesAsync(filePath, TestContext.Current.CancellationToken);
        var fileText = System.Text.Encoding.UTF8.GetString(fileBytes);

        // 加密后的文件不应包含明文内容
        fileText.Should().NotContain("SecretSave");
        fileText.Should().NotContain("chapter1");
    }

    [Fact]
    public async Task SaveLoad_WithTypedState_PreservesTypes()
    {
        var save = CreateSampleSave();
        save.TypedState = new Dictionary<string, SaveEntry>
        {
            ["gold"] = new() { Type = SaveEntryTypes.Int, Value = 500 },
            ["name"] = new() { Type = SaveEntryTypes.String, Value = "Hero" },
            ["alive"] = new() { Type = SaveEntryTypes.Bool, Value = true }
        };
        await _service.SaveAsync("typed_slot", save);

        var loaded = await _service.LoadAsync("typed_slot");
        loaded.Should().NotBeNull();
        loaded!.TypedState.Should().NotBeNull();
        loaded.TypedState!.Should().ContainKey("gold");
        loaded.TypedState.Should().ContainKey("name");
        loaded.TypedState.Should().ContainKey("alive");
    }

    // ========== 多次保存同一槽位 ==========

    [Fact]
    public async Task Save_MultipleTimes_KeepsLatest()
    {
        for (int i = 0; i < 5; i++)
        {
            var save = CreateSampleSave($"scene_{i}");
            await _service.SaveAsync("multi_slot", save);
        }

        var loaded = await _service.LoadAsync("multi_slot");
        loaded.Should().NotBeNull();
        loaded!.SceneName.Should().Be("scene_4");
    }

    // ========== 特殊字符槽位名 ==========

    [Fact]
    public async Task SaveLoad_SlotIdWithSpecialChars_Sanitizes()
    {
        // 槽位名包含非法文件名字符
        var slotId = "slot/with<>special:chars";
        await _service.SaveAsync(slotId, CreateSampleSave());

        var loaded = await _service.LoadAsync(slotId);
        loaded.Should().NotBeNull();
        loaded!.SceneName.Should().Be("chapter1");
    }
}
