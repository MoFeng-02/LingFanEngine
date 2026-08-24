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

    /// <summary>起始缩进块的关键字集合（折叠用，窄集）。</summary>
    public static IReadOnlySet<string> BlockStarters => _blockStarters;

    /// <summary>判断给定关键字是否起始一个缩进块（折叠语义）。</summary>
    public static bool IsBlockStarter(string keyword) => _blockStarters.Contains(keyword);

    /// <summary>
    /// 缩进规整（格式化）用开块关键字超集——较 <see cref="IsBlockStarter"/> 额外纳入
    /// label / menu / set_time_event / popup（这些在 DSL 中同样开启缩进块，折叠未覆盖）；
    /// 以及 case / default（switch 的多分支：缩进到 switch 的 body 级、其体更深，需压栈）。
    /// <para>注意 <b>else / else if 不在此集合</b>：它们在缩进上对齐到所属 if 同级（比体浅一层），
    /// 属「续行」而非「开块」，由 <see cref="IsIndentationContinuation"/> 单独判定、不压栈。</para>
    /// <para>折叠与格式化共用同一缩进栈算法，但开块集合不同：折叠把 else/case/default 视为续行以合并
    /// if/elif/else 为单一区域；格式化的开块集合不含 else，case/default 仍作开块以保证体缩进正确。两者不可混用。</para>
    /// </summary>
    private static readonly HashSet<string> _indentStarters = new()
    {
        "scene", "if", "while", "for", "func", "switch", "foreach",
        "label", "menu", "set_time_event", "popup",
        "case", "default",
    };

    /// <summary>缩进规整（格式化）用开块判定——<see cref="_indentStarters"/> 超集。</summary>
    public static bool IsIndentationBlockStarter(string keyword) => _indentStarters.Contains(keyword);

    /// <summary>
    /// 缩进续行判定（仅 else / else if）：这些关键字在缩进上对齐到其控制块（if）同级，
    /// 不开启新层级、不压栈，depth = 栈深 - 1。case/default 不在此，它们属开块（见 <see cref="IsIndentationBlockStarter"/>）。
    /// <para>DSL 的 <c>else if</c> 是带空格的整词，调用方取首词得到 "else" 即可命中。</para>
    /// </summary>
    public static bool IsIndentationContinuation(string keyword) => keyword is "else";

    /// <summary>
    /// 块结束关键字：弹出当前缩进栈顶。
    /// <para>目前仅 <c>end</c>（DSL 显式块结束）；<c>}</c>（花括号块结束）由格式化器直接处理。</para>
    /// </summary>
    public static bool IsIndentationBlockEnder(string keyword) => keyword is "end";
}
