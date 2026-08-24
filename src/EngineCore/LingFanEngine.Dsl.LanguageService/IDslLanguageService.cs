using System.Threading;
using System.Threading.Tasks;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// DSL 语言服务（UI 无关，LSP 式）——规划 §3.2。
/// <para>所有查询基于 offset/range（非整文件重扫）；跨文件符号索引支撑跳转定义 / 查找引用；纯逻辑、AOT 友好。</para>
/// <para>该接口日后可经 LSP 协议（Microsoft.VisualStudio.LanguageServer.Protocol + JSON-RPC）包一层，服务 VS Code / 任意 LSP 客户端。</para>
/// </summary>
public interface IDslLanguageService
{
    /// <summary>打开或更新文档。无脏区时全量重建；给定脏区时做行级增量重词法并同步重索引。</summary>
    void UpdateDocument(string filePath, string text, DirtyRange? dirty = null);

    /// <summary>从索引中移除文档（文件关闭 / 删除时调用）。</summary>
    void RemoveDocument(string filePath);

    /// <summary>定向刷新用：快照某文件当前定义的符号键（跨文件诊断）。</summary>
    System.Collections.Generic.HashSet<SymbolKey> SnapshotDefinitions(string path);

    /// <summary>定向刷新用：由定义前后快照求受影响的文件集合。</summary>
    System.Collections.Generic.HashSet<string> GetAffectedFilesByDefinitionChange(string path, System.Collections.Generic.HashSet<SymbolKey> before);

    /// <summary>批量索引整个项目的多文件，以满足跨文件跳转 / 查找引用的解析。</summary>
    void IndexProject(System.Collections.Generic.IReadOnlyList<(string Path, string Text)> files);

    /// <summary>扫描项目根，建联合资源索引（图片/音频/视频/字体等），供资源路径补全/悬停/跳转。</summary>
    void ScanProject(string rootPath);

    /// <summary>资源文件新增/变更（供 didChangeWatchedFiles 增量维护）。</summary>
    void UpdateResource(string absolutePath);

    /// <summary>资源文件删除（供 didChangeWatchedFiles 增量维护）。</summary>
    void RemoveResource(string absolutePath);

    /// <summary>语义高亮：返回 (offset, length, category) 数组，供前端一次性渲染。</summary>
    System.Collections.Generic.IReadOnlyList<SemanticToken> GetSemanticTokens(string filePath);

    /// <summary>基于位置的补全。</summary>
    System.Collections.Generic.IReadOnlyList<CompletionItem> GetCompletion(string filePath, int offset);

    /// <summary>基于位置的悬浮提示。</summary>
    HoverInfo? GetHover(string filePath, int offset);

    /// <summary>跳转定义（F12 / Ctrl+Click）。</summary>
    DefinitionResult GoToDefinition(string filePath, int offset);

    /// <summary>查找所有引用（Shift+F12）。</summary>
    ReferenceResult FindReferences(string filePath, int offset);

    /// <summary>文档大纲（textDocument/documentSymbol）：返回当前文件的场景/角色/标签/函数/样式/全局变量定义树（scene 含其内 label 子节点）。</summary>
    System.Collections.Generic.IReadOnlyList<DocumentOutlineSymbol> GetDocumentSymbols(string filePath);

    /// <summary>工作区符号搜索（workspace/symbol）：跨文件匹配定义名（query 为空返回全部），供 Ctrl+T 全局跳转。</summary>
    System.Collections.Generic.IReadOnlyList<WorkspaceSymbolInfo> GetWorkspaceSymbols(string query);

    /// <summary>文档高亮（textDocument/documentHighlight）：返回光标符号的全部定义/引用区间，复用查找引用结果。</summary>
    System.Collections.Generic.IReadOnlyList<HighlightSpan> GetDocumentHighlights(string filePath, int offset);

    /// <summary>重命名（textDocument/rename）：基于光标符号的「定义 + 全部引用」生成跨文件编辑；无符号可重命名时返回 null。</summary>
    RenameResult? Rename(string filePath, int offset, string newName);

    /// <summary>诊断（后台 + 取消令牌，非 UI 线程同步）。</summary>
    Task<DslAnalysisResult> GetDiagnosticsAsync(string filePath, CancellationToken ct = default);

    /// <summary>返回某符号种类下所有已定义名称（去重），供补全候选。</summary>
    System.Collections.Generic.IReadOnlyCollection<string> GetDefinedNames(SymbolKind kind);

    /// <summary>将字符偏移转换为 1-based 行/列，基于内存中已索引的文档（供前端跳转定位）。</summary>
    (int Line, int Column) GetLineColumn(string filePath, int offset);

    /// <summary>结构化块折叠区段（基于已索引文档 + DslCore 单源块结构）。返回 (StartOffset, EndOffset) 绝对字符偏移。</summary>
    System.Collections.Generic.IReadOnlyList<(int Start, int End)> GetFoldingRegions(string filePath);

    /// <summary>结构化块嵌套深度（基于已索引文档 + DslCore 单源块结构）；与 GetFoldingRegions 同源算法，保证折叠与格式化一致。长度 = 行数。</summary>
    int[] GetLineBlockDepths(string filePath);

    /// <summary>取某文件的规范源码文本（供跳转/悬停等把行/列坐标换算成偏移）。
    /// 优先 didOpen 内存文本，回退到工作区扫描建立的索引文档——确保「仅被扫描、尚未 didOpen 的文件」也能正确解析坐标。</summary>
    string? GetSource(string filePath);

    /// <summary>纯缩进规整（格式化）整篇文档——仅重排缩进与去尾随空白，保留内容与注释。供 textDocument/formatting。</summary>
    string? FormatDocument(string filePath, int? tabSize = null, bool insertSpaces = true);

    /// <summary>格式化指定行区间 [startLine, endLine]（闭区间，0-based）；范围外行原样保留。供 textDocument/rangeFormatting。</summary>
    string? FormatRange(string filePath, int startLine, int endLine, int? tabSize = null, bool insertSpaces = true);
}
