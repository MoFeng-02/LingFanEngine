namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 通知条目（用于通知队列）
/// <para>支持 info/warning/error 三种类型，每种类型有不同的颜色和图标。</para>
/// </summary>
public class NotificationItem
{
    /// <summary>通知文本</summary>
    public string Text { get; set; } = "";

    /// <summary>通知类型："info" / "warning" / "error"</summary>
    public string Type { get; set; } = "info";

    /// <summary>显示时长（秒），默认 3.0</summary>
    public double Duration { get; set; } = 3.0;
}
