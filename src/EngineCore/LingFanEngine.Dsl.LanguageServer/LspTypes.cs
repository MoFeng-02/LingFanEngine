using System.Text.Json;
using System.Text.Json.Serialization;

namespace LingFanEngine.Dsl.LanguageServer.Protocol;

/// <summary>
/// LSP 协议子集类型（手写、AOT 友好）——仅覆盖本服务实际使用的请求/响应。
/// <para>统一经 <see cref="LspJsonContext"/> 源生成序列化（零反射）；命名策略 camelCase，null 字段省略。</para>
/// </summary>
internal static class LspProtocol
{
    // 语义令牌图例：索引 == LingFanEngine.Dsl.LanguageService.SemanticCategory 枚举值（0..28）。
    // 与 DslLanguageService.GetSemanticTokens 产出的 tokenType 下标严格对齐。
    public static readonly string[] SemanticTokenLegend =
    {
        "unknown",      // 0 Unknown
        "comment",      // 1 Comment
        "string",       // 2 String
        "number",       // 3 Number
        "identifier",   // 4 Identifier
        "operator",     // 5 Symbol
        "keyword",      // 6 Keyword
        "keyword",      // 7 ControlFlow
        "keyword",      // 8 Navigation
        "keyword",      // 9 DataOp
        "keyword",      // 10 Media
        "keyword",      // 11 Display（显示/动画/Live2D）
        "keyword",      // 12 SaveLoad（存档系统）
        "keyword",      // 13 Chapter（章节/成就/图鉴）
        "keyword",      // 14 Rollback（回溯控制）
        "keyword",      // 15 Playback（播放控制增强）
        "keyword",      // 16 TimeEvent（时间事件系统）
        "keyword",      // 17 Notify（通知/调试）
        "keyword",      // 18 UiEnhance（UI增强）
        "property",     // 19 Parameter
        "property",     // 20 ElementAttribute
        "enumMember",   // 21 Literal
        "type",         // 22 UiDisplay
        "type",         // 23 UiContainer
        "function",     // 24 UiInteractive
        "function",     // 25 SymbolDefinition
        "variable",     // 26 SymbolReference
        "string",       // 27 Resource（资源路径引用）
        "function",     // 28 Function（表达式内置函数名）
    };
}

[JsonSerializable(typeof(Request))]
[JsonSerializable(typeof(Response))]
[JsonSerializable(typeof(Notification))]
[JsonSerializable(typeof(RpcError))]
[JsonSerializable(typeof(Position))]
[JsonSerializable(typeof(Range))]
[JsonSerializable(typeof(Location))]
[JsonSerializable(typeof(Location[]))]
[JsonSerializable(typeof(TextDocumentIdentifier))]
[JsonSerializable(typeof(TextDocumentItem))]
[JsonSerializable(typeof(VersionedTextDocumentIdentifier))]
[JsonSerializable(typeof(TextDocumentContentChangeEvent))]
[JsonSerializable(typeof(TextEdit))]
[JsonSerializable(typeof(TextDocumentPositionParams))]
[JsonSerializable(typeof(ReferenceContext))]
[JsonSerializable(typeof(ReferenceParams))]
[JsonSerializable(typeof(RenameParams))]
[JsonSerializable(typeof(DocumentSymbolParams))]
[JsonSerializable(typeof(WorkspaceSymbolParams))]
[JsonSerializable(typeof(DocumentHighlightParams))]
[JsonSerializable(typeof(WorkspaceEdit))]
[JsonSerializable(typeof(SymbolInformation))]
[JsonSerializable(typeof(SymbolInformation[]))]
[JsonSerializable(typeof(DocumentHighlight))]
[JsonSerializable(typeof(DocumentHighlight[]))]
[JsonSerializable(typeof(Dictionary<string, TextEdit[]>))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(FoldingRangeParams))]
[JsonSerializable(typeof(SemanticTokensParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(WorkspaceFolder))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(CompletionOptions))]
[JsonSerializable(typeof(SemanticTokensLegend))]
[JsonSerializable(typeof(SemanticTokensOptions))]
[JsonSerializable(typeof(Hover))]
[JsonSerializable(typeof(MarkupContent))]
[JsonSerializable(typeof(CompletionItem))]
[JsonSerializable(typeof(CompletionItem[]))]
[JsonSerializable(typeof(FoldingRange))]
[JsonSerializable(typeof(FoldingRange[]))]
[JsonSerializable(typeof(DocumentFormattingParams))]
[JsonSerializable(typeof(DocumentRangeFormattingParams))]
[JsonSerializable(typeof(FormattingOptions))]
[JsonSerializable(typeof(TextEdit[]))]
[JsonSerializable(typeof(SemanticTokens))]
[JsonSerializable(typeof(LspDiagnostic))]
[JsonSerializable(typeof(PublishDiagnosticsParams))]
[JsonSerializable(typeof(DidChangeWatchedFilesParams))]
[JsonSerializable(typeof(FileEvent))]
[JsonSerializable(typeof(WorkspaceCapabilities))]
[JsonSerializable(typeof(FileOperations))]
[JsonSerializable(typeof(DidChangeWatchedFiles))]
[JsonSerializable(typeof(FileOperationFilter))]
[JsonSerializable(typeof(FileOperationPattern))]
[JsonSerializable(typeof(WindowCapabilities))]
[JsonSerializable(typeof(LogMessageParams))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false)]
internal sealed partial class LspJsonContext : JsonSerializerContext;

/// <summary>JSON-RPC 2.0 请求（也用于通知：此时 <see cref="Id"/> 为 null）。</summary>
internal sealed class Request
{
    public string Jsonrpc { get; set; } = "2.0";
    public JsonElement? Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public JsonElement? Params { get; set; }

    /// <summary>运行时字段（非协议）：写请求在「入队」时构建的写链任务；worker 仅等待其完成（handler 已在链内于写锁下执行）。</summary>
    [JsonIgnore] public Task? ChainTask;
    /// <summary>运行时字段（非协议）：读请求在「入队」时快照的写链尾（其之前入队的所有写），用于保证读晚于前序写执行。</summary>
    [JsonIgnore] public Task? Barrier;
}

/// <summary>JSON-RPC 2.0 成功响应。</summary>
internal sealed class Response
{
    public string Jsonrpc { get; set; } = "2.0";
    public JsonElement? Id { get; set; }
    public JsonElement? Result { get; set; }
    public JsonElement? Error { get; set; }
}

/// <summary>JSON-RPC 2.0 服务端→客户端通知（如 publishDiagnostics）。</summary>
internal sealed class Notification
{
    public string Jsonrpc { get; set; } = "2.0";
    public string Method { get; set; } = string.Empty;
    public JsonElement? Params { get; set; }
}

/// <summary>JSON-RPC 错误对象。</summary>
internal sealed class RpcError
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
}

internal sealed class Position
{
    public int Line { get; set; }
    public int Character { get; set; }
}

internal sealed class Range
{
    public Position Start { get; set; } = new();
    public Position End { get; set; } = new();
}

internal sealed class TextEdit
{
    public Range Range { get; set; } = new();
    public string NewText { get; set; } = string.Empty;
}

internal sealed class Location
{
    public string Uri { get; set; } = string.Empty;
    public Range Range { get; set; } = new();
}

internal sealed class TextDocumentIdentifier
{
    public string Uri { get; set; } = string.Empty;
}

internal sealed class TextDocumentItem
{
    public string Uri { get; set; } = string.Empty;
    public string? LanguageId { get; set; }
    public int? Version { get; set; }
    public string Text { get; set; } = string.Empty;
}

internal sealed class VersionedTextDocumentIdentifier
{
    public string Uri { get; set; } = string.Empty;
    public int? Version { get; set; }
}

internal sealed class TextDocumentContentChangeEvent
{
    public Range? Range { get; set; }
    public int? RangeLength { get; set; }
    public string Text { get; set; } = string.Empty;
}

internal sealed class TextDocumentPositionParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public Position Position { get; set; } = new();
}

internal sealed class ReferenceContext
{
    public bool IncludeDeclaration { get; set; }
}

internal sealed class ReferenceParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public Position Position { get; set; } = new();
    public ReferenceContext Context { get; set; } = new();
}

internal sealed class RenameParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public Position Position { get; set; } = new();
    public string NewName { get; set; } = string.Empty;
}

internal sealed class DocumentSymbolParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

internal sealed class WorkspaceSymbolParams
{
    public string? Query { get; set; }
}

internal sealed class DocumentHighlightParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public Position Position { get; set; } = new();
}

internal sealed class DidOpenTextDocumentParams
{
    public TextDocumentItem TextDocument { get; set; } = new();
}

internal sealed class DidChangeTextDocumentParams
{
    public VersionedTextDocumentIdentifier TextDocument { get; set; } = new();
    public TextDocumentContentChangeEvent[] ContentChanges { get; set; } = [];
}

internal sealed class FoldingRangeParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

internal sealed class SemanticTokensParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
}

/// <summary>格式化选项：tabSize = 每级缩进空格数；insertSpaces = true 用空格、false 用制表符。</summary>
internal sealed class FormattingOptions
{
    public int TabSize { get; set; } = 2;
    public bool InsertSpaces { get; set; } = true;
}

internal sealed class DocumentFormattingParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public FormattingOptions Options { get; set; } = new();
}

internal sealed class DocumentRangeFormattingParams
{
    public TextDocumentIdentifier TextDocument { get; set; } = new();
    public FormattingOptions Options { get; set; } = new();
    public Range Range { get; set; } = new();
}

internal sealed class ServerInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
}

internal sealed class CompletionOptions
{
    public string[]? TriggerCharacters { get; set; }
    public bool? ResolveProvider { get; set; }
}

internal sealed class SemanticTokensLegend
{
    public string[] TokenTypes { get; set; } = [];
    public string[] TokenModifiers { get; set; } = [];
}

internal sealed class SemanticTokensOptions
{
    public SemanticTokensLegend Legend { get; set; } = new();
    public bool? Full { get; set; }
}

internal sealed class ServerCapabilities
{
    /// <summary>1 = Full（didChange 发送整文）。</summary>
    public int? TextDocumentSync { get; set; }
    public bool? HoverProvider { get; set; }
    public bool? DefinitionProvider { get; set; }
    public bool? ReferencesProvider { get; set; }
    public bool? FoldingRangeProvider { get; set; }
    public CompletionOptions? CompletionProvider { get; set; }
    public SemanticTokensOptions? SemanticTokensProvider { get; set; }
    /// <summary>整文档格式化（textDocument/formatting）。</summary>
    public bool? DocumentFormattingProvider { get; set; }
    /// <summary>选区格式化（textDocument/rangeFormatting）。</summary>
    public bool? DocumentRangeFormattingProvider { get; set; }
    /// <summary>workspace 能力（如文件监听，用于 P2 增量索引）。</summary>
    public WorkspaceCapabilities? Workspace { get; set; }
    /// <summary>window 能力（如 workDoneProgress，用于 P4 进度通知）。</summary>
    public WindowCapabilities? Window { get; set; }
    /// <summary>重命名（textDocument/rename）。</summary>
    public bool? RenameProvider { get; set; }
    /// <summary>文档大纲（textDocument/documentSymbol）。</summary>
    public bool? DocumentSymbolProvider { get; set; }
    /// <summary>工作区符号搜索（workspace/symbol）。</summary>
    public bool? WorkspaceSymbolProvider { get; set; }
    /// <summary>文档高亮（textDocument/documentHighlight）。</summary>
    public bool? DocumentHighlightProvider { get; set; }
}

internal sealed class InitializeResult
{
    public ServerCapabilities Capabilities { get; set; } = new();
    public ServerInfo? ServerInfo { get; set; }
}

internal sealed class WorkspaceFolder
{
    public string Uri { get; set; } = string.Empty;
    public string? Name { get; set; }
}

/// <summary>initialize 请求参数（仅消费本服务用得上的字段；capabilities 暂忽略）。</summary>
internal sealed class InitializeParams
{
    /// <summary>父进程 PID（LSP 规范为 number|null；用于进程生命周期探测，本服务暂仅接收不主动使用）。</summary>
    public int? ProcessId { get; set; }
    /// <summary>工作区根 URI（通常 file://...）。优先于 rootPath。</summary>
    public string? RootUri { get; set; }
    public string? RootPath { get; set; }
    public WorkspaceFolder[]? WorkspaceFolders { get; set; }
}

internal sealed class MarkupContent
{
    public string Kind { get; set; } = "markdown";
    public string Value { get; set; } = string.Empty;
}

internal sealed class Hover
{
    public MarkupContent? Contents { get; set; }
    public Range? Range { get; set; }
}

internal sealed class WorkspaceEdit
{
    /// <summary>uri → 文本编辑数组（LSP WorkspaceEdit.changes）。</summary>
    public Dictionary<string, TextEdit[]>? Changes { get; set; }
}

internal sealed class SymbolInformation
{
    public string Name { get; set; } = string.Empty;
    public int Kind { get; set; }
    public Location Location { get; set; } = new();
}

internal sealed class DocumentHighlight
{
    public Range Range { get; set; } = new();
    /// <summary>1=Text, 2=Read, 3=Write。本服务统一给 2。</summary>
    public int? Kind { get; set; }
}

internal sealed class CompletionItem
{
    public string Label { get; set; } = string.Empty;
    public int? Kind { get; set; }
    public string? Detail { get; set; }
    public string? InsertText { get; set; }
    public TextEdit? TextEdit { get; set; }
}

internal sealed class FoldingRange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int? StartCharacter { get; set; }
    public int? EndCharacter { get; set; }
    public string? Kind { get; set; }
}

internal sealed class SemanticTokens
{
    public int[] Data { get; set; } = [];
}

internal sealed class LspDiagnostic
{
    public Range Range { get; set; } = new();
    public int? Severity { get; set; }
    public string? Source { get; set; }
    public string Message { get; set; } = string.Empty;
}

internal sealed class PublishDiagnosticsParams
{
    public string Uri { get; set; } = string.Empty;
    public LspDiagnostic[] Diagnostics { get; set; } = [];
}

// ---- P2: workspace/didChangeWatchedFiles 文件监听（增量重索引）----

internal sealed class DidChangeWatchedFilesParams
{
    public FileEvent[] Changes { get; set; } = [];
}

/// <summary>LSP FileEvent：Uri + 变更类型（1=Created, 2=Changed, 3=Deleted）。</summary>
internal sealed class FileEvent
{
    public string Uri { get; set; } = string.Empty;
    public int Type { get; set; }
}

internal sealed class WorkspaceCapabilities
{
    public FileOperations? FileOperations { get; set; }
}

internal sealed class FileOperations
{
    public DidChangeWatchedFiles? DidChangeWatchedFiles { get; set; }
}

internal sealed class DidChangeWatchedFiles
{
    public FileOperationFilter[] Filters { get; set; } = [];
}

internal sealed class FileOperationFilter
{
    public FileOperationPattern? Pattern { get; set; }
}

internal sealed class FileOperationPattern
{
    public string? Glob { get; set; }
    public string? Matches { get; set; }
}

// ---- P4: window/logMessage 进度/诊断通知（服务端→客户端）----

internal sealed class WindowCapabilities
{
    public bool? WorkDoneProgress { get; set; }
}

internal sealed class LogMessageParams
{
    /// <summary>1=Error, 2=Warning, 3=Info, 4=Log。</summary>
    public int Type { get; set; }
    public string Message { get; set; } = string.Empty;
}
