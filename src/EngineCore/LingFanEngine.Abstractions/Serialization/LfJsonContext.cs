using System.Text.Json.Serialization;
using LingFanEngine.Abstractions.Entities;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.Medias;
// Router 已移除
using LingFanEngine.Abstractions.Entities.Scenes;
using LingFanEngine.Abstractions.Entities.Transitions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Cores;
using LingFanEngine.Abstractions.Models.Npcs;
using LingFanEngine.Abstractions.Models.Players;
using LingFanEngine.Abstractions.Models.Saves;

namespace LingFanEngine.Abstractions.Serialization;

/// <summary>
/// 灵泛引擎 JsonAOT 序列化上下文
/// <para>注册所有 Entity 和 Model 类型，支持 Native AOT 编译时的零反射序列化。</para>
/// </summary>
[JsonSerializable(typeof(BaseEntity))]
[JsonSerializable(typeof(TimeEventEntity))]
[JsonSerializable(typeof(MediaEntity))]
[JsonSerializable(typeof(SceneEntity))]
[JsonSerializable(typeof(UIElementEntity))]
[JsonSerializable(typeof(BaseModel))]
[JsonSerializable(typeof(NumericalValue))]
[JsonSerializable(typeof(NpcCore))]
[JsonSerializable(typeof(PlayerCore))]
[JsonSerializable(typeof(SaveData))]
[JsonSerializable(typeof(SceneSnapshot))]
[JsonSerializable(typeof(SaveSlotInfo))]
[JsonSerializable(typeof(SaveEntry))]
[JsonSerializable(typeof(RollbackCheckpoint))]
[JsonSerializable(typeof(TransitionEntity))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, SaveEntry>))]
[JsonSerializable(typeof(HashSet<int>))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(List<UIElementEntity>))]
[JsonSerializable(typeof(DialogHistoryEntry))]
[JsonSerializable(typeof(List<DialogHistoryEntry>))]
[JsonSerializable(typeof(GalleryEntry))]
[JsonSerializable(typeof(List<GalleryEntry>))]
[JsonSerializable(typeof(DebugLogEntry))]
[JsonSerializable(typeof(List<DebugLogEntry>))]
[JsonSerializable(typeof(PackManifest))]
[JsonSerializable(typeof(AchievementEntry))]
[JsonSerializable(typeof(List<AchievementEntry>))]
[JsonSerializable(typeof(ChapterEntry))]
[JsonSerializable(typeof(List<ChapterEntry>))]
[JsonSerializable(typeof(List<TimeEventEntity>))]
[JsonSerializable(typeof(TimeEventSaveState))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement
)]
public partial class LfJsonContext : JsonSerializerContext;
