namespace LingFanEngine.DslCore;

/// <summary>
/// 补全 snippet 模板——按语句关键字提供默认展开内容。
/// <para>模板中的 <c>{indent}</c> 占位符由 <see cref="TryGetSnippet"/> 替换为当前行缩进。</para>
/// <para>原 SDK 在 CodeEditorView 的 switch 中硬编码，现收归 DslCore 单一真相源，便于统一维护。</para>
/// </summary>
public static class DslSnippetProvider
{
    private static readonly Dictionary<string, string> _snippets = new()
    {
        ["say"] = "\"\" speaker=\"\"",
        ["scene"] = "\"\" type=game\n{indent}    ",
        ["label"] = "",
        ["if"] = "{true}\n{indent}    ",
        ["while"] = "{true}\n{indent}    ",
        ["for"] = "\"i\" in {0..10}\n{indent}    ",
        ["menu"] = "\"选择\" {\n{indent}    \"选项1\" -> label1,\n{indent}    \"选项2\" -> label2,\n{indent}}",
        ["character"] = "\"key\" name=\"名字\" color=\"#FFFFFF\"",
        ["style"] = "\"name\" color=#FFFFFF size=18",
        ["navigate"] = "\"scene_name\"",
        ["background"] = "\"path/to/bg.png\"",
        ["bgm"] = "\"path/to/music.ogg\" volume=0.8",
        ["transition"] = "\"fade\" duration=1.0",
        ["show"] = "\"target\"",
        ["hide"] = "\"target\"",
        ["animate"] = "\"target\" property=\"x\" target=100 duration=1.0",
        ["sprite"] = "\"id\" src=\"path.png\"",
    };

    /// <summary>
    /// 尝试获取关键字的 snippet 展开结果。
    /// <paramref name="indent"/> 为当前行缩进，替换模板中的 <c>{indent}</c>。
    /// 命中返回 <see langword="true"/>（<paramref name="snippet"/> 可能为空白字符串，如 label）；
    /// 未命中返回 <see langword="false"/>（<paramref name="snippet"/> 为 null）。
    /// </summary>
    public static bool TryGetSnippet(string keyword, string indent, out string? snippet)
    {
        if (_snippets.TryGetValue(keyword, out var template))
        {
            snippet = template.Replace("{indent}", indent);
            return true;
        }
        snippet = null;
        return false;
    }
}
