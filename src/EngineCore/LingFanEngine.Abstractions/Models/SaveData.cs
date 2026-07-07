using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Models.Saves;

namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 存档数据
/// <para>包含全量用户状态 + 完整 SceneStack 快照。</para>
/// <para>状态恢复后前后跳转功能正常，所有用户变量完整还原。</para>
/// </summary>
public class SaveData
{
    /// <summary>存档唯一标识</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>游戏版本号（用于存档兼容性校验）</summary>
    public required string GameVersion { get; set; }

    /// <summary>存档名称</summary>
    public string? Name { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>修改时间</summary>
    public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>存档时的场景名</summary>
    public required string SceneName { get; set; }

    /// <summary>全量用户状态（不含 __ 系统变量）</summary>
    public Dictionary<string, object?> State { get; set; } = new();

    /// <summary>
    /// 带类型标识的状态字典（V2 格式）
    /// <para>新存档同时写入 State（兼容）和 TypedState（类型安全）。</para>
    /// <para>加载时优先使用 TypedState，为空则回退到 State + ConvertJsonValue。</para>
    /// </summary>
    public Dictionary<string, SaveEntry>? TypedState { get; set; }

    /// <summary>SceneStack 完整快照（后退/前进历史）</summary>
    public List<SceneSnapshot> SceneStackSnapshot { get; set; } = new();

    /// <summary>
    /// DSL 执行位置（当前命令索引），用于读档后恢复执行
    /// <para>-1 表示非 DSL 场景或无需恢复。</para>
    /// </summary>
    public int DslCurrentIndex { get; set; } = -1;

    /// <summary>
    /// DSL 阻塞类型（存档时的 waiting type），用于读档后恢复阻塞状态
    /// <para>"dialog" / "menu" / "input" / "wait" / "" </para>
    /// </summary>
    public string? DslWaitingType { get; set; }

    /// <summary>存档缩略图（JPEG bytes，320×180，由 SceneView.CaptureThumbnail 生成）</summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>
    /// 存档时的场景类型
    /// <para>仅 Game 场景允许存档；Menu/UI 场景不应出现在存档中。</para>
    /// </summary>
    public SceneType SceneType { get; set; } = SceneType.Game;
}

