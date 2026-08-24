namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// LSP 特性结果类型（服务层、UI 无关）——与 LSP 协议类型解耦，由 <c>DslLanguageServer</c> 映射为协议类型。
/// <para>纯数据、NativeAOT 友好；此层类型不参与 JSON 序列化（仅协议层类型经 <c>LspJsonContext</c> 源生成）。</para>
/// </summary>
public sealed class DocumentOutlineSymbol
{
    /// <summary>符号名（scene/label/func/character/style/变量 的声明名）。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>LSP SymbolKind 数值（见 <c>DslLanguageService.MapSymbolKindToLsp</c>）。</summary>
    public int Kind { get; set; }
    /// <summary>声明名起始偏移（字符索引），供 server 换算 range。</summary>
    public int StartOffset { get; set; }
    /// <summary>声明名结尾偏移（StartOffset + 名称长度），供 server 换算 range。</summary>
    public int EndOffset { get; set; }
    /// <summary>符号所在文件绝对路径。空（默认）= 沿用当前文档路径（单文件符号）；跨文件符号（如并入大纲的其它文件场景）须填自身路径，server 据此正确定位。</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>子符号（如 scene 节点下的 label），构成大纲层级。</summary>
    public List<DocumentOutlineSymbol> Children { get; set; } = new();
}

/// <summary>workspace/symbol 结果项（跨文件符号）。</summary>
public sealed class WorkspaceSymbolInfo
{
    public string Name { get; set; } = string.Empty;
    public int Kind { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int Offset { get; set; }
    public int Length { get; set; }
}

/// <summary>documentHighlight 高亮区间（定义/引用统一高亮）。</summary>
public sealed class HighlightSpan
{
    public int Offset { get; set; }
    public int Length { get; set; }
    /// <summary>1=Text, 2=Read, 3=Write（本服务统一给 2，定义项亦框为可读高亮）。</summary>
    public int Kind { get; set; }
}

/// <summary>textDocument/rename 结果：按文件路径归组的重命名编辑。</summary>
public sealed class RenameResult
{
    /// <summary>key = 文件绝对路径（由 server 换算为 file:// URI）；value = 该文件内的重命名编辑列表。</summary>
    public Dictionary<string, List<RenameEdit>> Changes { get; set; } = new();
}

/// <summary>单处重命名编辑（替换名区间为新名）。</summary>
public sealed class RenameEdit
{
    public int Offset { get; set; }
    public int Length { get; set; }
    public string NewText { get; set; } = string.Empty;
}
