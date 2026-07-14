using System;
using System.Collections.Generic;

namespace LingFanEngine.SDK.Models;

/// <summary>DSL 分析结果</summary>
public class DslAnalysisResult
{
    /// <summary>是否分析成功（无错误）</summary>
    public bool Success { get; set; }

    /// <summary>错误列表</summary>
    public List<DslDiagnostic> Errors { get; set; } = new();

    /// <summary>警告列表</summary>
    public List<DslDiagnostic> Warnings { get; set; } = new();

    /// <summary>信息级诊断列表（P1-1：未使用变量等提示）</summary>
    public List<DslDiagnostic> Infos { get; set; } = new();

    /// <summary>提取的变量列表</summary>
    public List<VariableInfo> Variables { get; set; } = new();

    /// <summary>跨文件引用列表</summary>
    public List<SceneReference> References { get; set; } = new();

    /// <summary>AST 语句列表（List&lt;DslStatement&gt;，运行时强类型转换）</summary>
    public object? Ast { get; set; }

    /// <summary>分析耗时</summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>源文件路径</summary>
    public string FilePath { get; set; } = "";
}

/// <summary>诊断严重级别</summary>
public enum DiagnosticSeverity
{
    Error,
    Warning,
    /// <summary>信息级（P1-1：未使用变量等提示）</summary>
    Info
}

/// <summary>诊断信息（错误/警告）</summary>
public record DslDiagnostic(int Line, int Column, string Message, string? SourceLine, DiagnosticSeverity Severity = DiagnosticSeverity.Error);

/// <summary>变量信息</summary>
public record VariableInfo(string Name, string? Value, int DefinitionLine, List<int> ReferenceLines);

/// <summary>场景引用</summary>
public record SceneReference(string SourceFile, int Line, string TargetScene, string? TargetLabel, ReferenceType Type);

/// <summary>引用类型</summary>
public enum ReferenceType
{
    Navigate,
    Jump,
    Call,
    Scene
}

/// <summary>构建结果</summary>
public class BuildResult
{
    public bool Success { get; set; }
    public string OutputPath { get; set; } = "";
    public string Platform { get; set; } = "";
    public List<string> Logs { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>资源条目</summary>
public record AssetEntry(string Path, string RelativePath, string FileName, AssetType Type, long SizeBytes);

/// <summary>资源类型</summary>
public enum AssetType
{
    Image,
    Audio,
    Video,
    Story,
    Json,
    Other
}

/// <summary>资源预览信息</summary>
public record AssetPreview(string Path, AssetType Type, string? ThumbnailPath, string? Info);
