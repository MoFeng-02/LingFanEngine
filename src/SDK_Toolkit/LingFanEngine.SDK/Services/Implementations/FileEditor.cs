using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 工程级文件编辑器实现。
/// <para>原子写：同卷临时文件 → <see cref="File.Move(string,string,bool)"/> 覆盖，避免写一半损坏；
/// 提交前自动把现有文件备份为 <c>{path}.bak</c>，供 <see cref="RollbackAsync"/> 恢复。</para>
/// </summary>
public sealed class FileEditor : IFileEditor
{
    private static readonly Encoding s_utf8NoBom = new UTF8Encoding(false);

    /// <inheritdoc/>
    public Task<FileSnapshot> ReadAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(path))
        {
            return Task.FromResult(new FileSnapshot
            {
                Path = path,
                Content = "",
                Length = 0,
                Hash = "",
                Exists = false,
            });
        }

        var content = File.ReadAllText(path, s_utf8NoBom);
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return Task.FromResult(new FileSnapshot
        {
            Path = path,
            Content = content,
            Length = bytes.Length,
            Hash = hash,
            Exists = true,
        });
    }

    /// <inheritdoc/>
    public FileEdit Build(string path, string? oldContent, string newContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        newContent ??= "";
        return new FileEdit
        {
            Path = path,
            OldContent = oldContent,   // 保留 null 以正确标志"新建文件"（不见强转为空串）
            NewContent = newContent,
            IsNoop = oldContent != null && string.Equals(oldContent, newContent, StringComparison.Ordinal),
        };
    }

    /// <inheritdoc/>
    public async Task ApplyAsync(FileEdit edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        // 新旧一致——无写入，直接标记完成
        if (edit.IsNoop)
        {
            edit.Applied = true;
            edit.AppliedAt = DateTime.Now;
            return;
        }

        ct.ThrowIfCancellationRequested();
        var dir = Path.GetDirectoryName(edit.Path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // 提交前备份现有文件
        string? backup = null;
        if (File.Exists(edit.Path))
        {
            backup = edit.Path + ".bak";
            File.Copy(edit.Path, backup, overwrite: true);
            edit.BackupPath = backup;
        }

        // 原子写：同卷临时文件 → Move 覆盖
        var temp = edit.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temp, edit.NewContent, s_utf8NoBom, ct).ConfigureAwait(false);
        try
        {
            File.Move(temp, edit.Path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        edit.Applied = true;
        edit.AppliedAt = DateTime.Now;
    }

    /// <inheritdoc/>
    public Task<bool> RollbackAsync(FileEdit edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!edit.Applied || edit.RolledBack)
            return Task.FromResult(false);

        ct.ThrowIfCancellationRequested();
        if (edit.BackupPath != null && File.Exists(edit.BackupPath))
        {
            File.Copy(edit.BackupPath, edit.Path, overwrite: true);
        }
        else if (edit.IsNewFile)
        {
            // 新建文件回滚 = 删除（无备份）
            TryDelete(edit.Path);
        }

        edit.RolledBack = true;
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public string RenderDiff(FileEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.IsNoop)
            return "(无变更)";

        var sb = new StringBuilder();
        sb.Append("--- ").Append(edit.Path).Append('\n');
        sb.Append("+++ ").Append(edit.Path).Append("\n\n");

        // 新建文件：只列新增行
        if (edit.IsNewFile)
        {
            foreach (var l in SplitLines(edit.NewContent))
                sb.Append("+ ").Append(l).Append('\n');
            return sb.ToString();
        }

        var oldLines = SplitLines(edit.OldContent ?? "");
        var newLines = SplitLines(edit.NewContent);

        // 大文件不做 O(n·m) LCS，退化为"全量重写"
        const int maxLcs = 4000;
        if (oldLines.Length > maxLcs || newLines.Length > maxLcs)
        {
            foreach (var l in oldLines)
                sb.Append("- ").Append(l).Append('\n');
            foreach (var l in newLines)
                sb.Append("+ ").Append(l).Append('\n');
            return sb.ToString();
        }

        foreach (var (op, line) in LcsDiff(oldLines, newLines))
            sb.Append(op).Append(' ').Append(line).Append('\n');

        return sb.ToString();
    }

    private static string[] SplitLines(string s)
        => s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    /// <summary>基于 LCS 的逐行 diff，返回 (操作符, 行) 序列；op ∈ {' ', '-', '+'}。</summary>
    private static (char Op, string Line)[] LcsDiff(string[] a, string[] b)
    {
        if (a.Length == 0)
            return b.Select(x => ('+', x)).ToArray();
        if (b.Length == 0)
            return a.Select(x => ('-', x)).ToArray();

        var m = a.Length;
        var n = b.Length;
        var dp = new int[m + 1, n + 1];
        for (var i = 1; i <= m; i++)
            for (var j = 1; j <= n; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        var result = new System.Collections.Generic.List<(char, string)>(m + n);
        var x = m;
        var y = n;
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1])
            {
                result.Add((' ', a[x - 1]));
                x--;
                y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
            {
                result.Add(('-', a[x - 1]));
                x--;
            }
            else
            {
                result.Add(('+', b[y - 1]));
                y--;
            }
        }
        while (x > 0)
        {
            result.Add(('-', a[x - 1]));
            x--;
        }
        while (y > 0)
        {
            result.Add(('+', b[y - 1]));
            y--;
        }
        result.Reverse();
        return result.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 尽力而为
        }
    }
}