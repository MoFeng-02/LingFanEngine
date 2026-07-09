using LingFanEngine.Abstractions.Models.Saves;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 场景堆栈接口
/// <para>管理历史导航记录，支持前后跳转和完整状态回退。</para>
/// <para>每次导航时保存当前场景名 + 世界状态快照。</para>
/// </summary>
public interface ISceneStack
{
    /// <summary>最大堆栈深度（超出时丢弃最旧记录）</summary>
    int MaxDepth { get; set; }

    /// <summary>当前后退堆栈深度</summary>
    int Count { get; }

    /// <summary>当前前进堆栈深度</summary>
    int ForwardCount { get; }

    /// <summary>获取完整堆栈快照（用于存档）</summary>
    IReadOnlyList<SceneSnapshot> Snapshot { get; }

    /// <summary>推入当前场景快照（导航前调）</summary>
    void Push(string sceneName);

    /// <summary>后退：弹出栈顶，恢复其场景和状态</summary>
    SceneSnapshot? Back();

    /// <summary>前进：恢复之前后退时弹出的状态</summary>
    SceneSnapshot? Forward();

    /// <summary>查看上一个场景（不弹出）</summary>
    SceneSnapshot? Peek();

    /// <summary>清空堆栈（scene 命令时调）</summary>
    void Clear();

    /// <summary>用快照恢复堆栈（读档时调）</summary>
    void Restore(IReadOnlyList<SceneSnapshot> snapshot);
}
