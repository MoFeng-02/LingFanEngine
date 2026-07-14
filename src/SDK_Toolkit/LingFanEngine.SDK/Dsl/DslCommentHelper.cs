using System;

namespace LingFanEngine.SDK.Dsl;

/// <summary>
/// DSL 注释处理工具——与引擎核心 LingFanDslEngine.StripInlineComment 保持一致。
/// <para>支持两种注释风格：</para>
/// <list type="bullet">
/// <item># 行注释（整行）</item>
/// <item>// 行注释（整行或行尾，引号外检测避免误切 URL）</item>
/// </list>
/// </summary>
public static class DslCommentHelper
{
    /// <summary>判断是否为注释行（# 或 // 开头）</summary>
    public static bool IsCommentLine(string trimmedLine)
    {
        return trimmedLine.StartsWith('#') || trimmedLine.StartsWith("//");
    }

    /// <summary>
    /// 剥离行尾注释——检测引号外的 // 并截断。
    /// <para>规则：跟踪引号开闭状态，仅在引号外遇到 // 时截断。</para>
    /// <para>这避免了误切 URL 路径 "http://..." 中的 //。</para>
    /// </summary>
    public static string StripInlineComment(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"')
                inQuotes = !inQuotes;
            else if (!inQuotes && line[i] == '/' && line[i + 1] == '/')
                return line[..i].TrimEnd();
        }
        return line;
    }

    /// <summary>
    /// 清理行：剥离行尾注释 + Trim。
    /// <para>如果清理后为空或为注释行，返回空字符串。</para>
    /// </summary>
    public static string CleanLine(string rawLine)
    {
        var trimmed = StripInlineComment(rawLine.Trim());
        if (string.IsNullOrEmpty(trimmed) || IsCommentLine(trimmed))
            return "";
        return trimmed;
    }
}
