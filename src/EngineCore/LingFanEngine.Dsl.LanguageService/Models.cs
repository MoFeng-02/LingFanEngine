namespace LingFanEngine.Dsl.LanguageService;

/// <summary>源码位置——文件 + 字符偏移 + 长度（也可派生出行/列）。</summary>
public readonly struct Location
{
    public string FilePath { get; }
    public int Offset { get; }
    public int Length { get; }

    public Location(string filePath, int offset, int length)
    {
        FilePath = filePath;
        Offset = offset;
        Length = length;
    }
}

/// <summary>脏区描述——一次编辑影响 [Start, Start+OldLength) 并被新文本替换。</summary>
public readonly struct DirtyRange
{
    public int Start { get; }
    public int OldLength { get; }
    public int NewLength { get; }

    public DirtyRange(int start, int oldLength, int newLength)
    {
        Start = start;
        OldLength = oldLength;
        NewLength = newLength;
    }
}

/// <summary>补全项。</summary>
public sealed class CompletionItem
{
    /// <summary>插入文本（如关键字或符号名）。</summary>
    public string InsertText { get; set; }
    /// <summary>展示标签（可与 InsertText 不同）。</summary>
    public string DisplayText { get; }
    /// <summary>排序/分组用种类提示（statement/label/scene/variable/parameter...）。</summary>
    public string Kind { get; }
    /// <summary>可选说明。</summary>
    public string? Detail { get; }
    /// <summary>富文本文档（markdown，悬浮详情/补全面板文档区）。</summary>
    public string? Documentation { get; set; }
    /// <summary>排序键——前缀匹配优先、已定义符号优先、关键字次之。</summary>
    public string? SortText { get; set; }
    /// <summary>过滤键——客户端据此做前缀/模糊匹配。</summary>
    public string? FilterText { get; set; }
    /// <summary>是否预选中（最佳匹配高亮）。</summary>
    public bool Preselect { get; set; }
    /// <summary>插入后自动提交的字符（如空格、= 等）。</summary>
    public string? CommitCharacters { get; set; }

    /// <summary>补全替换起点（绝对字符偏移）。-1 表示由调用方自行按词边界探测。
    /// 用于资源路径/命令名等含分隔符（/ 或 _）的候选，避免把已输入前缀重复拼回（如 "Audio/cri" → 选 "Audio/x.mp3" 不会变 "Audio/Audio/x.mp3"）。</summary>
    public int ReplaceStart { get; set; } = -1;

    public CompletionItem(string insertText, string displayText, string kind, string? detail = null, int replaceStart = -1)
    {
        InsertText = insertText;
        DisplayText = displayText;
        Kind = kind;
        Detail = detail;
        ReplaceStart = replaceStart;
    }
}

/// <summary>悬浮提示信息。</summary>
public sealed class HoverInfo
{
    public string Title { get; }
    public string? Detail { get; }
    /// <summary>若悬浮在符号上，给出其定义/引用位置（用于 UI 链接）。</summary>
    public Location? RelatedLocation { get; }

    public HoverInfo(string title, string? detail = null, Location? relatedLocation = null)
    {
        Title = title;
        Detail = detail;
        RelatedLocation = relatedLocation;
    }
}

/// <summary>跳转定义结果。</summary>
public sealed class DefinitionResult
{
    public bool Found { get; }
    public Location? Location { get; }
    public SymbolKind Kind { get; }

    public DefinitionResult(bool found, Location? location, SymbolKind kind)
    {
        Found = found;
        Location = location;
        Kind = kind;
    }
}

/// <summary>查找所有引用结果。</summary>
public sealed class ReferenceResult
{
    public IReadOnlyList<Location> Locations { get; }
    public SymbolKind Kind { get; }
    public string Name { get; }

    public ReferenceResult(IReadOnlyList<Location> locations, SymbolKind kind, string name)
    {
        Locations = locations;
        Kind = kind;
        Name = name;
    }
}

/// <summary>诊断严重级别。</summary>
public enum DiagnosticSeverity
{
    Hint = 0,
    Info,
    Warning,
    Error,
}

/// <summary>单条诊断。</summary>
public sealed class Diagnostic
{
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public Location Location { get; }

    public Diagnostic(DiagnosticSeverity severity, string message, Location location)
    {
        Severity = severity;
        Message = message;
        Location = location;
    }
}

/// <summary>整文件分析结果。</summary>
public sealed class DslAnalysisResult
{
    public string FilePath { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public DslAnalysisResult(string filePath, IReadOnlyList<Diagnostic> diagnostics)
    {
        FilePath = filePath;
        Diagnostics = diagnostics;
    }
}

/// <summary>参数签名信息（供 signatureHelp）。</summary>
public sealed class SignatureHelpInfo
{
    public IReadOnlyList<SignatureInfo> Signatures { get; }
    public int? ActiveSignature { get; }
    public int? ActiveParameter { get; }

    public SignatureHelpInfo(IReadOnlyList<SignatureInfo> signatures, int? activeSignature = null, int? activeParameter = null)
    {
        Signatures = signatures;
        ActiveSignature = activeSignature;
        ActiveParameter = activeParameter;
    }
}

public sealed class SignatureInfo
{
    public string Label { get; }
    public string? Documentation { get; }
    public IReadOnlyList<ParameterInfo> Parameters { get; }

    public SignatureInfo(string label, string? documentation, IReadOnlyList<ParameterInfo> parameters)
    {
        Label = label;
        Documentation = documentation;
        Parameters = parameters;
    }
}

public sealed class ParameterInfo
{
    public string Label { get; }
    public string? Documentation { get; }

    public ParameterInfo(string label, string? documentation = null)
    {
        Label = label;
        Documentation = documentation;
    }
}
