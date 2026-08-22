using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>资源管理服务实现</summary>
public class AssetManager : IAssetManager
{
    private static readonly string[] s_imageExts = ProjectConstants.ImageExtensions;
    private static readonly string[] s_audioExts = ProjectConstants.AudioExtensions;
    private static readonly string[] s_videoExts = ProjectConstants.VideoExtensions;

    private static readonly HashSet<string> s_assetExts = new(
        ProjectConstants.ImageExtensions
            .Concat(ProjectConstants.AudioExtensions)
            .Concat(ProjectConstants.VideoExtensions)
            .Append(ProjectConstants.StoryExt)
            .Append(ProjectConstants.JsonExt),
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex s_quoted = new("\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex s_assetRefHint = new(@"[\w./-]+\.[A-Za-z0-9]+$", RegexOptions.Compiled);

    /// <inheritdoc/>
    public async Task<AssetDiagnostics> DiagnoseAsync(string projectDir)
    {
        var diag = new AssetDiagnostics();
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in await ScanAssetsAsync(projectDir).ConfigureAwait(false))
            existing.Add(e.Path);

        var resourcesRoot = Path.Combine(projectDir, ProjectConstants.ResourcesDir);
        var sourceRoot = Directory.Exists(resourcesRoot) ? resourcesRoot : projectDir;

        foreach (var storyFile in Directory.GetFiles(projectDir, "*.story", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(storyFile)) continue;

            string content;
            try { content = File.ReadAllText(storyFile); }
            catch { continue; }

            var relativeStory = Path.GetRelativePath(projectDir, storyFile);
            foreach (Match m in s_quoted.Matches(content))
            {
                var token = m.Groups[1].Value;
                if (!LooksLikeAssetRef(token)) continue;
                if (ResolveExisting(token, sourceRoot, projectDir, existing) == null)
                    diag.Issues.Add(new AssetReferenceIssue(relativeStory, token, "项目中未找到该资源"));
            }
        }

        return diag;
    }

    /// <summary>粗略判定引号内文本疑似资源路径（带已知资源扩展名、无空格）。</summary>
    private static bool LooksLikeAssetRef(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length < 2 || token.Contains(' '))
            return false;
        var ext = Path.GetExtension(token);
        if (string.IsNullOrEmpty(ext) || !s_assetExts.Contains(ext))
            return false;
        return s_assetRefHint.IsMatch(token);
    }

    /// <summary>解析引用令牌是否命中现存文件：ResourcesRoot → 项目根 → 绝对路径 → 按文件名匹配。</summary>
    private static string? ResolveExisting(string token, string sourceRoot, string projectDir, HashSet<string> existing)
    {
        var candidates = new[]
        {
            Path.Combine(sourceRoot, token),
            Path.Combine(projectDir, token),
            Path.GetFullPath(token),
        };
        foreach (var c in candidates)
            if (File.Exists(c))
                return c;

        var name = Path.GetFileName(token);
        if (name.Length > 0)
        {
            foreach (var p in existing)
                if (string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase))
                    return p;
        }
        return null;
    }

    /// <inheritdoc/>
    public Task<List<AssetEntry>> ScanAssetsAsync(string projectDir)
    {
        var entries = new List<AssetEntry>();
        var roots = EnumerateAssetRoots(projectDir, out var projectRoot);
        foreach (var dir in roots)
            ScanDirectory(dir, projectRoot, entries);
        return Task.FromResult(entries);
    }

    /// <inheritdoc/>
    public Task<AssetTree> BuildAssetTreeAsync(string projectDir)
    {
        var roots = EnumerateAssetRoots(projectDir, out _);
        var tree = new AssetTree();
        foreach (var dir in roots)
            tree.Roots.Add(BuildDirNode(dir));
        return Task.FromResult(tree);
    }

    /// <summary>
    /// 解析实际要扫描的资源根目录列表（只枚举目录，不枚举文件）。
    /// <para>标准布局 <c>{scanRoot}/{Stories,Media,Images,Audio,Video,Live2D}</c>（排除 Lang 翻译数据）；
    /// 仅当全部缺失时才从项目根兜底扫描同名目录（兼容异常目录结构/模板嵌套，且跳过构建产物与已扫路径）。</para>
    /// </summary>
    private static List<string> EnumerateAssetRoots(string projectDir, out string projectRoot)
    {
        // 真实资源目录 = ResourceDirNames（Stories/Media/Images/Audio/Video/Live2D），排除 Lang（翻译数据非资源）。
        // 注意：不用旧的 AssetScanDirs=[Stories,Media,Assets]——Assets 为遗留/不存在的目录，
        // 且缺了顶层 Images/Audio/Video（真实放置处），会导致媒体资源扫不到。
        var assetDirs = ProjectConstants.ResourceDirNames
            .Where(n => !string.Equals(n, ProjectConstants.LangDir, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // 标准布局：{projectDir}/Resources/{Stories,Media,Assets}
        // （ViewModel 传入 ResourcesDirectory；直接传项目根则拼 Resources 前缀）
        var isResourcesDir = Path.GetFileName(projectDir) == ProjectConstants.ResourcesDir;
        var scanRoot = isResourcesDir
            ? projectDir
            : Path.Combine(projectDir, ProjectConstants.ResourcesDir);
        projectRoot = isResourcesDir
            ? Path.GetDirectoryName(projectDir) ?? projectDir
            : projectDir;

        var dirs = new List<string>();
        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(scanRoot))
        {
            foreach (var subDir in assetDirs)
            {
                var fullPath = Path.Combine(scanRoot, subDir);
                if (!Directory.Exists(fullPath))
                    continue;
                scanned.Add(fullPath);
                dirs.Add(fullPath);
            }
        }

        // 兜底：仅当标准资源根缺失时从项目根全树扫描，跳过构建产物与已扫的标准路径
        if (dirs.Count == 0 && Directory.Exists(projectRoot))
        {
            foreach (var subDir in assetDirs)
                foreach (var dir in Directory.GetDirectories(projectRoot, subDir, SearchOption.AllDirectories))
                {
                    if (IsBuildArtifact(dir) || scanned.Contains(dir))
                        continue;
                    dirs.Add(dir);
                }
        }

        return dirs;
    }

    /// <summary>递归构建目录节点（子目录在前、文件在后）。</summary>
    private static AssetNode BuildDirNode(string dir)
    {
        var node = new AssetNode { Name = Path.GetFileName(dir), Path = dir, IsDirectory = true };
        foreach (var f in Directory.GetFiles(dir))
        {
            var info = new FileInfo(f);
            node.Children.Add(new AssetNode
            {
                Name = info.Name,
                Path = f,
                IsDirectory = false,
                Type = GetAssetType(Path.GetExtension(f).ToLowerInvariant()),
                SizeBytes = info.Length,
            });
        }
        foreach (var sd in Directory.GetDirectories(dir))
            node.Children.Add(BuildDirNode(sd));
        return node;
    }

    /// <summary>排除构建产物目录（obj/bin）</summary>
    private static bool IsBuildArtifact(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase);
    }

    private static int ScanDirectory(string dir, string projectDir, List<AssetEntry> entries)
    {
        var count = 0;
        foreach (var file in Directory.GetFiles(dir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var type = GetAssetType(ext);
            var info = new FileInfo(file);
            var relativePath = Path.GetRelativePath(projectDir, file);

            entries.Add(new AssetEntry(file, relativePath, info.Name, type, info.Length));
            count++;
        }

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            count += ScanDirectory(subDir, projectDir, entries);
        }
        return count;
    }

    /// <inheritdoc/>
    public async Task ImportAssetAsync(string projectDir, string sourceFile, string targetSubDir)
    {
        var targetDir = Path.Combine(projectDir, targetSubDir);
        Directory.CreateDirectory(targetDir);

        var fileName = Path.GetFileName(sourceFile);
        var targetPath = Path.Combine(targetDir, fileName);

        // 使用 FileStream + CopyToAsync 实现真正的异步文件复制（File.Copy 无异步 API）
        const int bufferSize = 81920;
        using var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await source.CopyToAsync(target);
    }

    /// <inheritdoc/>
    public Task DeleteAssetAsync(string assetPath)
    {
        if (File.Exists(assetPath))
            File.Delete(assetPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<AssetPreview?> GetPreviewAsync(string assetPath)
    {
        if (!File.Exists(assetPath))
            return Task.FromResult<AssetPreview?>(null);

        var ext = Path.GetExtension(assetPath).ToLowerInvariant();
        var type = GetAssetType(ext);
        var info = new FileInfo(assetPath);

        var preview = type switch
        {
            AssetType.Image => new AssetPreview(assetPath, type, assetPath, $"{info.Length / 1024.0:F1} KB"),
            AssetType.Audio => new AssetPreview(assetPath, type, null, $"音频文件 {info.Length / 1024.0:F1} KB"),
            AssetType.Video => new AssetPreview(assetPath, type, null, $"视频文件 {info.Length / 1024.0:F1} KB"),
            AssetType.Story => new AssetPreview(assetPath, type, null, $"故事文件 {info.Length / 1024.0:F1} KB"),
            _ => new AssetPreview(assetPath, type, null, $"{info.Length / 1024.0:F1} KB"),
        };

        return Task.FromResult<AssetPreview?>(preview);
    }

    private static AssetType GetAssetType(string ext)
    {
        if (s_imageExts.Contains(ext)) return AssetType.Image;
        if (s_audioExts.Contains(ext)) return AssetType.Audio;
        if (s_videoExts.Contains(ext)) return AssetType.Video;
        if (ext == ProjectConstants.StoryExt) return AssetType.Story;
        if (ext == ProjectConstants.JsonExt) return AssetType.Json;
        return AssetType.Other;
    }
}
