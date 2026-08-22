using System.Collections.Generic;

namespace LingFanEngine.SDK.Models;

/// <summary>资源目录树节点（目录或文件）。</summary>
public sealed class AssetNode
{
    /// <summary>节点名（文件名或目录名）</summary>
    public required string Name { get; init; }

    /// <summary>绝对路径</summary>
    public required string Path { get; init; }

    /// <summary>是否目录</summary>
    public bool IsDirectory { get; init; }

    /// <summary>文件资源类型（目录时为 <see cref="AssetType.Other"/>）</summary>
    public AssetType Type { get; init; }

    /// <summary>文件大小（字节；目录为 0）</summary>
    public long SizeBytes { get; init; }

    /// <summary>子节点（目录才有）</summary>
    public List<AssetNode> Children { get; init; } = new();
}

/// <summary>资源目录树——按资源根（Stories/Media/Assets）组织文件夹层级。</summary>
public sealed class AssetTree
{
    /// <summary>顶层资源根节点</summary>
    public List<AssetNode> Roots { get; init; } = new();
}

/// <summary>资源引用问题（某 story 引用了项目中不存在的资源）。</summary>
public sealed record AssetReferenceIssue(string StoryFile, string Referenced, string Detail);

/// <summary>资源引用/缺失诊断结果。</summary>
public sealed class AssetDiagnostics
{
    /// <summary>缺失引用问题列表</summary>
    public List<AssetReferenceIssue> Issues { get; } = new();
}