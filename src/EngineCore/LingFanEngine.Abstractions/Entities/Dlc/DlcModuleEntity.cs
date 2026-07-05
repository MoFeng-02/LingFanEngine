using System.Collections.Generic;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.Scenes;

namespace LingFanEngine.Abstractions.Entities.Dlc;

public class DlcModuleEntity : BaseEntity
{
    public required string ModuleId { get; set; }
    public required string ModuleName { get; set; }
    public required string Version { get; set; }
    public string? Author { get; set; }
    public List<SceneEntity> Scenes { get; set; } = [];
    public List<TimeEventEntity> Events { get; set; } = [];
    public string? NativeLibraryPath { get; set; }
    public bool IsEnabled { get; set; }
}
