namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 对话历史条目（对标 Ren'Py HistoryEntry）
/// <para>每次 say 命令执行时由 DialogHistoryService 记录一条。</para>
/// <para>UI 层通过 StateKeys.History.Entries 读取 List&lt;DialogHistoryEntry&gt; 渲染回看面板。</para>
/// </summary>
public class DialogHistoryEntry
{
    /// <summary>说话者名（可能为空，表示旁白）</summary>
    public string? Speaker { get; set; }

    /// <summary>对话文本（已替换表达式后的纯文本）</summary>
    public string Text { get; set; } = "";

    /// <summary>记录时间戳</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>当前场景名（记录时所在场景）</summary>
    public string? SceneName { get; set; }

    /// <summary>回溯检查点索引（用于从历史面板跳转到该 Say）</summary>
    public int CheckpointIndex { get; set; } = -1;
}
