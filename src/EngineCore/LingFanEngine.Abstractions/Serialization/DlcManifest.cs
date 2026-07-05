using System.Text.Json.Serialization;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.Scenes;

namespace LingFanEngine.Abstractions.Serialization;

public class DlcManifest
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("version")]
    public required string Version { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("entryPoint")]
    public string EntryPoint { get; set; } = "InitializePlugin";

    [JsonPropertyName("scenes")]
    public List<SceneEntity> Scenes { get; set; } = [];

    [JsonPropertyName("events")]
    public List<TimeEventEntity> Events { get; set; } = [];

    /// <summary>
    /// 原生库路径（相对路径，如 "plugins/module.dll"）。
    /// <para>PluginLoader 会根据 RID 自动选择正确的平台子目录。</para>
    /// </summary>
    [JsonPropertyName("nativeLibraryPath")]
    public string? NativeLibraryPath { get; set; }
}
