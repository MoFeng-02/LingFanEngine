using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Cores;
using LingFanEngine.Abstractions.Models.Enums;
using LingFanEngine.Abstractions.Models.Npcs;
using LingFanEngine.Abstractions.Models.Players;
using LingFanEngine.Abstractions.Models.Saves;
using Xunit;

namespace LingFanEngine.Tests.Models;

/// <summary>
/// 数据模型构造测试：实例化 Abstractions.Models 下所有数据类（含 required 属性、默认值、枚举成员、
/// SaveEntryTypes 常量），锁定数据契约并提升 Abstractions.Models 包覆盖率。
/// </summary>
public class ModelConstructionTests
{
    [Fact]
    public void BaseModel_HasIdAndTimestamps()
    {
        var m = new BaseModel();
        m.Id.Should().NotBe(Guid.Empty);
        m.CreateTime.Should().NotBe(default);
        m.UpdateTime.Should().NotBe(default);
    }

    [Fact]
    public void AchievementEntry_DefaultsAndSetters()
    {
        var a = new AchievementEntry { Id = "a1", Name = "First Blood", UnlockedAt = DateTimeOffset.UnixEpoch };
        a.Id.Should().Be("a1");
        a.Name.Should().Be("First Blood");
        a.UnlockedAt.Should().Be(DateTimeOffset.UnixEpoch);

        new AchievementEntry().Id.Should().Be("");
    }

    [Fact]
    public void ChapterEntry_Defaults()
    {
        var c = new ChapterEntry { Id = "c1", Name = "Ch1", Unlocked = false };
        c.Id.Should().Be("c1");
        c.Unlocked.Should().BeFalse();

        new ChapterEntry().Unlocked.Should().BeTrue();
    }

    [Fact]
    public void DebugLogEntry_Defaults()
    {
        var d = new DebugLogEntry { Level = "Error", Message = "boom", Source = "x" };
        d.Level.Should().Be("Error");
        d.Message.Should().Be("boom");
        d.Source.Should().Be("x");

        new DebugLogEntry().Level.Should().Be("Info");
    }

    [Fact]
    public void DialogHistoryEntry_Defaults()
    {
        var h = new DialogHistoryEntry { Speaker = "Alice", Text = "hi", SceneName = "s", CheckpointIndex = 3 };
        h.Speaker.Should().Be("Alice");
        h.Text.Should().Be("hi");
        h.SceneName.Should().Be("s");
        h.CheckpointIndex.Should().Be(3);

        new DialogHistoryEntry().Text.Should().Be("");
        new DialogHistoryEntry().CheckpointIndex.Should().Be(-1);
    }

    [Fact]
    public void EngineLogEntry_RecordDefaults()
    {
        var e = new EngineLogEntry { Category = "Loader", Message = "loaded", Level = EngineLogLevel.Warning };
        e.Category.Should().Be("Loader");
        e.Message.Should().Be("loaded");
        e.Level.Should().Be(EngineLogLevel.Warning);

        new EngineLogEntry().Category.Should().Be("");
    }

    [Fact]
    public void GalleryEntry_Defaults()
    {
        var g = new GalleryEntry { Id = "g1", ImagePath = "cg.png", Title = "T", SceneName = "s" };
        g.Id.Should().Be("g1");
        g.ImagePath.Should().Be("cg.png");
        g.Title.Should().Be("T");
        g.SceneName.Should().Be("s");

        new GalleryEntry().ImagePath.Should().Be("");
    }

    [Fact]
    public void NotificationItem_Defaults()
    {
        var n = new NotificationItem { Text = "hi", Type = "warning", Duration = 5.0 };
        n.Text.Should().Be("hi");
        n.Type.Should().Be("warning");
        n.Duration.Should().Be(5.0);

        new NotificationItem().Type.Should().Be("info");
        new NotificationItem().Duration.Should().Be(3.0);
    }

    [Fact]
    public void NumericalValue_RequiresValues()
    {
        var v = new NumericalValue
        {
            CoreId = Guid.NewGuid(),
            CharacterKind = CharacterKind.Player,
            Values = new() { ["hp"] = 100 },
        };
        v.CoreId.Should().NotBe(Guid.Empty);
        v.CharacterKind.Should().Be(CharacterKind.Player);
        v.Values.Should().ContainKey("hp");

        new NumericalValue { Values = new() }.CoreId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void PackManifest_Defaults()
    {
        var p = new PackManifest { Name = "ch1", Version = "2.0", EngineVersion = "1.0", Files = ["a.lf"], Description = "d" };
        p.Name.Should().Be("ch1");
        p.Version.Should().Be("2.0");
        p.EngineVersion.Should().Be("1.0");
        p.Files.Should().ContainSingle().Which.Should().Be("a.lf");
        p.Description.Should().Be("d");

        new PackManifest().Version.Should().Be("1.0.0");
    }

    [Fact]
    public void RollbackCheckpoint_Defaults()
    {
        var r = new RollbackCheckpoint
        {
            CommandIndex = 5,
            SceneName = "s",
            InteractionType = "menu",
            StateSnapshot = new() { ["k"] = 1 },
        };
        r.CommandIndex.Should().Be(5);
        r.SceneName.Should().Be("s");
        r.InteractionType.Should().Be("menu");
        r.StateSnapshot.Should().ContainKey("k");

        new RollbackCheckpoint().InteractionType.Should().Be("dialog");
    }

    [Fact]
    public void SaveData_RequiresGameVersionAndSceneName()
    {
        var d = new SaveData
        {
            GameVersion = "1.0.0",
            SceneName = "title",
            Name = "save1",
            State = new() { ["gold"] = 10 },
            DslCurrentIndex = 7,
            DslWaitingType = "dialog",
            Thumbnail = new byte[] { 1, 2 },
        };
        d.GameVersion.Should().Be("1.0.0");
        d.SceneName.Should().Be("title");
        d.State.Should().ContainKey("gold");
        d.DslCurrentIndex.Should().Be(7);
        d.DslWaitingType.Should().Be("dialog");
        d.Thumbnail.Should().Equal(1, 2);

        new SaveData { GameVersion = "x", SceneName = "y" }.DslCurrentIndex.Should().Be(-1);
    }

    [Fact]
    public void SystemSaveData_Defaults()
    {
        var s = new SystemSaveData();
        s.GameVersion.Should().Be("1.0.0");
        s.MasterVolume.Should().Be(1.0f);
        s.MasterMuted.Should().BeFalse();
        s.BgmVolume.Should().Be(0.8f);
        s.SeVolume.Should().Be(1.0f);
        s.VoiceVolume.Should().Be(1.0f);
        s.DefaultTextSpeed.Should().Be(60);
    }

    [Fact]
    public void RouterState_PrimaryConstructor_RequiresPath()
    {
        var r = new RouterState
        {
            Path = "route/a",
            CurrentSceneIndex = 2,
            SceneStates = [new SceneState { SceneName = "s" }],
        };
        r.Path.Should().Be("route/a");
        r.CurrentSceneIndex.Should().Be(2);
        r.SceneStates.Should().ContainSingle();
    }

    [Fact]
    public void SaveEntry_And_SaveEntryTypes_Constants()
    {
        var e = new SaveEntry { Type = SaveEntryTypes.Int, Value = 42 };
        e.Type.Should().Be("int");
        e.Value.Should().Be(42);

        new SaveEntry().Type.Should().Be(SaveEntryTypes.String);

        SaveEntryTypes.Int.Should().Be("int");
        SaveEntryTypes.Long.Should().Be("long");
        SaveEntryTypes.Double.Should().Be("double");
        SaveEntryTypes.Float.Should().Be("float");
        SaveEntryTypes.Bool.Should().Be("bool");
        SaveEntryTypes.String.Should().Be("string");
        SaveEntryTypes.Null.Should().Be("null");
        SaveEntryTypes.ListUIElement.Should().Be("list_ui");
        SaveEntryTypes.DictStringObject.Should().Be("dict_str_obj");
        SaveEntryTypes.Decimal.Should().Be("decimal");
        SaveEntryTypes.DateTime.Should().Be("datetime");
        SaveEntryTypes.Guid.Should().Be("guid");
    }

    [Fact]
    public void SaveSlotInfo_RequiresSlotId()
    {
        var s = new SaveSlotInfo { SlotId = "slot1", Name = "n", GameVersion = "1.0", Thumbnail = new byte[] { 9 } };
        s.SlotId.Should().Be("slot1");
        s.Name.Should().Be("n");
        s.GameVersion.Should().Be("1.0");
        s.Thumbnail.Should().Equal(9);
    }

    [Fact]
    public void SceneSnapshot_RequiresSceneName()
    {
        var s = new SceneSnapshot { SceneName = "s", State = new() { ["a"] = 1 } };
        s.SceneName.Should().Be("s");
        s.State.Should().ContainKey("a");

        new SceneSnapshot { SceneName = "default" }.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void SceneState_RequiresSceneName()
    {
        var s = new SceneState { SceneName = "s", InteractionStates = new() { ["i1"] = true } };
        s.SceneName.Should().Be("s");
        s.InteractionStates.Should().ContainKey("i1");
    }

    [Fact]
    public void NpcCore_RequiresName()
    {
        var n = new NpcCore { Name = "Bob", Gender = "M", Description = "desc" };
        n.Name.Should().Be("Bob");
        n.Gender.Should().Be("M");
        n.Description.Should().Be("desc");
    }

    [Fact]
    public void PlayerCore_RequiresName()
    {
        var p = new PlayerCore { Name = "Hero", Gender = "F" };
        p.Name.Should().Be("Hero");
        p.Gender.Should().Be("F");
    }

    [Fact]
    public void CharacterKind_EnumValues()
    {
        ((int)CharacterKind.Player).Should().Be(0);
        ((int)CharacterKind.NPC).Should().Be(1);
    }
}
