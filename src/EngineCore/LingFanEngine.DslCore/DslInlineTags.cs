namespace LingFanEngine.DslCore;

/// <summary>
/// 行内富文本标记（{b}{/b}{i}{/i}{w}{fast}{p} 及 color= / font= / size= 前缀）。
/// <para>原 SDK 在 Highlighter / DiagnosticCollector 各写一份 IsInlineTag，现统一收归 DslCore 单一真相源。</para>
/// </summary>
public static class DslInlineTags
{
    private static readonly HashSet<string> _tags = new()
    {
        "b", "/b", "i", "/i", "w", "fast", "p",
    };

    private static readonly string[] _prefixed =
    {
        "color=", "/color", "font=", "/font", "size=", "/size",
    };

    /// <summary>完整行内标记名集合（无前缀的短标记）。</summary>
    public static IReadOnlySet<string> Tags => _tags;

    /// <summary>判断给定内容（{...} 内去掉首尾空白后的文本）是否为已知的行内标记。</summary>
    public static bool IsInlineTag(string content)
    {
        if (_tags.Contains(content)) return true;
        foreach (var prefix in _prefixed)
        {
            if (content.StartsWith(prefix)) return true;
        }
        return false;
    }
}
