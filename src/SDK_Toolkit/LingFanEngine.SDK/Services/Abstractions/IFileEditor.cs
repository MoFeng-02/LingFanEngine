using System;
using System.Threading;
using System.Threading.Tasks;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>
/// 文件快照——<see cref="IFileEditor.ReadAsync"/> 的产物，记录路径与当前内容（含内容哈希）。
/// </summary>
public sealed class FileSnapshot
{
    /// <summary>文件路径</summary>
    public string Path { get; init; } = "";

    /// <summary>文件内容（UTF-8 文本）</summary>
    public string Content { get; init; } = "";

    /// <summary>内容字节长度</summary>
    public int Length { get; init; }

    /// <summary>内容 SHA-256（十六进制小写）</summary>
    public string Hash { get; init; } = "";

    /// <summary>文件是否已存在（false = 不存在的文件快照）</summary>
    public bool Exists { get; init; }
}

/// <summary>
/// 文件编辑——描述对某个文件的一次"旧→新"变更，可原子提交、可回滚。
/// <para>由 <see cref="IFileEditor.Build"/> 生成（不做 I/O）；<see cref="IFileEditor.ApplyAsync"/> 提交后填充 Applied/BackupPath。</para>
/// </summary>
public sealed class FileEdit
{
    /// <summary>目标文件路径</summary>
    public string Path { get; init; } = "";

    /// <summary>旧内容（null = 新建文件）</summary>
    public string? OldContent { get; init; }

    /// <summary>新内容</summary>
    public string NewContent { get; init; } = "";

    /// <summary>是否新建文件（旧内容为 null）</summary>
    public bool IsNewFile => OldContent == null;

    /// <summary>无实际变更（新旧一致）——无需提交，可直跳过</summary>
    public bool IsNoop { get; init; }

    /// <summary>提交前的备份文件路径（提交后填充）</summary>
    public string? BackupPath { get; internal set; }

    /// <summary>是否已提交</summary>
    public bool Applied { get; internal set; }

    /// <summary>提交时间</summary>
    public DateTime? AppliedAt { get; internal set; }

    /// <summary>是否已回滚</summary>
    public bool RolledBack { get; internal set; }
}

/// <summary>
/// 工程级文件编辑器——统一"读 → 改 → diff → 原子提交 → 备份/回滚"的文件写入契约。
/// <para>供翻译/资源/生成/构建诊断等一切"改用户项目文件"的副作用复用；
/// 高副作用写入应在提交前经 <see cref="RenderDiff"/> 展示给人环审批。</para>
/// <para>实现要点：同卷临时文件 + 原子 <see cref="System.IO.File.Move"/> 覆盖，提交前自动 <c>.bak</c> 备份。</para>
/// </summary>
public interface IFileEditor
{
    /// <summary>读取文件快照；文件不存在返回 <c>Exists=false</c> 的快照（非 null）。</summary>
    Task<FileSnapshot> ReadAsync(string path, CancellationToken ct = default);

    /// <summary>构建一次文件编辑（纯计算，不做 I/O）。</summary>
    /// <param name="path">目标文件路径</param>
    /// <param name="oldContent">旧内容（null = 新建；可由 <see cref="ReadAsync"/> 的 Content/Exists 得出）</param>
    /// <param name="newContent">新内容</param>
    FileEdit Build(string path, string? oldContent, string newContent);

    /// <summary>原子提交一次编辑：写临时文件 → 校验 → 原子覆盖；原文件先备份成 <c>{path}.bak</c>。</summary>
    /// <param name="edit">由 <see cref="Build"/> 创建；<see cref="FileEdit.IsNoop"/> 时直接标记完成不写盘。</param>
    Task ApplyAsync(FileEdit edit, CancellationToken ct = default);

    /// <summary>回滚一次已提交的编辑（用 <see cref="FileEdit.BackupPath"/> 恢复；新建文件则删除）。</summary>
    Task<bool> RollbackAsync(FileEdit edit, CancellationToken ct = default);

    /// <summary>渲染人读 diff（行级 +/-），供审批预览。</summary>
    string RenderDiff(FileEdit edit);
}