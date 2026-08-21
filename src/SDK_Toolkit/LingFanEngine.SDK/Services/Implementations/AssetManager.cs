using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <inheritdoc/>
    public Task<List<AssetEntry>> ScanAssetsAsync(string projectDir)
    {
        var entries = new List<AssetEntry>();
        var assetDirs = ProjectConstants.AssetScanDirs;

        // 标准布局：{projectDir}/Resources/{Stories,Media,Assets}
        // （ViewModel 传入的是 ResourcesDirectory；直接传入项目根时拼 Resources 前缀）
        var isResourcesDir = Path.GetFileName(projectDir) == ProjectConstants.ResourcesDir;
        var scanRoot = isResourcesDir
            ? projectDir
            : Path.Combine(projectDir, ProjectConstants.ResourcesDir);
        // 项目根：兜底扫描的起点（传入 ResourcesDirectory 时为其父目录）
        var projectRoot = isResourcesDir
            ? Path.GetDirectoryName(projectDir) ?? projectDir
            : projectDir;

        var foundAny = false;
        if (Directory.Exists(scanRoot))
        {
            foreach (var subDir in assetDirs)
            {
                var fullPath = Path.Combine(scanRoot, subDir);
                if (!Directory.Exists(fullPath))
                    continue;
                var count = ScanDirectory(fullPath, projectRoot, entries);
                foundAny |= count > 0;
            }
        }

        // 兜底：标准布局未找到任何资源时，从项目根扫描所有 Stories/Media/Assets 目录
        // （兼容模板内容嵌套/异常目录结构）
        if (!foundAny && Directory.Exists(projectRoot))
        {
            foreach (var subDir in assetDirs)
            {
                foreach (var dir in Directory.GetDirectories(projectRoot, subDir, SearchOption.AllDirectories))
                {
                    if (IsBuildArtifact(dir) || dir == scanRoot)
                        continue;
                    ScanDirectory(dir, projectRoot, entries);
                }
            }
        }

        return Task.FromResult(entries);
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
