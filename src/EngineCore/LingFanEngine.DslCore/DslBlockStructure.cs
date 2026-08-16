namespace LingFanEngine.DslCore;

/// <summary>
/// 块结构元数据——哪些语句关键字起始一个缩进块。
/// <para>原 SDK 在 CodeEditorView（自动缩进）与 DiagnosticCollector（块结构检查）各硬编码一份，
/// 现统一收归 DslCore 单一真相源。</para>
/// </summary>
public static class DslBlockStructure
{
    private static readonly HashSet<string> _blockStarters = new()
    {
        "scene", "if", "while", "for", "func", "switch", "foreach",
        // 注意：else/case/default 是对块栈的匹配而非起始块，不在此集合内。
    };

    /// <summary>起始缩进块的关键字集合。</summary>
    public static IReadOnlySet<string> BlockStarters => _blockStarters;

    /// <summary>判断给定关键字是否起始一个缩进块。</summary>
    public static bool IsBlockStarter(string keyword) => _blockStarters.Contains(keyword);
}
