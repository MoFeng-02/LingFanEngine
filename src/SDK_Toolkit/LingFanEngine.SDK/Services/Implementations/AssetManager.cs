using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>资源管理服务实现</summary>
public class AssetManager : IAssetManager
{
    private static readonly string[] s_imageExts = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];
    private static readonly string[] s_audioExts = [".mp3", ".ogg", ".wav", ".flac", ".m4a"];
    private static readonly string[] s_videoExts = [".mp4", ".webm", ".avi", ".mkv"];

    /// <inheritdoc/>
    public Task<List<AssetEntry>> ScanAssetsAsync(string projectDir)
    {
        var entries = new List<AssetEntry>();
        var assetDirs = new[] { "Stories", "Media", "Assets" };

        foreach (var subDir in assetDirs)
        {
            var fullPath = Path.Combine(projectDir, subDir);
            if (!Directory.Exists(fullPath))
                continue;

            ScanDirectory(fullPath, projectDir, entries);
        }

        return Task.FromResult(entries);
    }

    private static void ScanDirectory(string dir, string projectDir, List<AssetEntry> entries)
    {
        foreach (var file in Directory.GetFiles(dir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var type = GetAssetType(ext);
            var info = new FileInfo(file);
            var relativePath = Path.GetRelativePath(projectDir, file);

            entries.Add(new AssetEntry(file, relativePath, info.Name, type, info.Length));
        }

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            ScanDirectory(subDir, projectDir, entries);
        }
    }

    /// <inheritdoc/>
    public async Task ImportAssetAsync(string projectDir, string sourceFile, string targetSubDir)
    {
        var targetDir = Path.Combine(projectDir, targetSubDir);
        Directory.CreateDirectory(targetDir);

        var fileName = Path.GetFileName(sourceFile);
        var targetPath = Path.Combine(targetDir, fileName);

        await Task.Run(() => File.Copy(sourceFile, targetPath, overwrite: true));
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
        if (ext == ".story") return AssetType.Story;
        if (ext == ".json") return AssetType.Json;
        return AssetType.Other;
    }
}
