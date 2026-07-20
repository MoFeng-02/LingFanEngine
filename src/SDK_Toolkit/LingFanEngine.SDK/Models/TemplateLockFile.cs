namespace LingFanEngine.SDK.Models;

/// <summary>
/// 模板缓存版本真相声明（template-cache/template.lock.json）。
/// <para>记录当前使用的模板版本与来源（内置 / 下载），供版本比对与离线判定。</para>
/// </summary>
public class TemplateLockFile
{
    /// <summary>当前模板版本（X.Y.Z）。</summary>
    public string TemplateVersion { get; set; } = "0.0.0";

    /// <summary>来源：builtin（SDK 内置嵌入模板）/ download（从 Release 下载覆盖）。</summary>
    public string Source { get; set; } = "builtin";

    /// <summary>最近检查 / 更新时间（UTC ISO8601）。</summary>
    public string LastCheckedUtc { get; set; } = "";
}
