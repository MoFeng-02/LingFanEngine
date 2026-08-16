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
    public string InsertText { get; }
    /// <summary>展示标签（可与 InsertText 不同）。</summary>
    public string DisplayText { get; }
    /// <summary>排序/分组用种类提示（statement/label/scene/variable/parameter...）。</summary>
    public string Kind { get; }
    /// <summary>可选说明。</summary>
    public string? Detail { get; }

    public CompletionItem(string insertText, string displayText, string kind, string? detail = null)
    {
        InsertText = insertText;
        DisplayText = displayText;
        Kind = kind;
        Detail = detail;
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
