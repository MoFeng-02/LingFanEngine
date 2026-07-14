using System.IO;
using System.Threading.Tasks;

namespace LingFanEngine.SDK.Utils;

/// <summary>文件系统操作封装</summary>
public static class FileHelper
{
    /// <summary>读取文本文件</summary>
    public static async Task<string> ReadAllTextAsync(string path)
    {
        return await File.ReadAllTextAsync(path);
    }

    /// <summary>写入文本文件</summary>
    public static async Task WriteAllTextAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, content);
    }

    /// <summary>递归复制目录</summary>
    public static async Task CopyDirectoryAsync(string srcDir, string destDir)
    {
        PathHelper.EnsureDirectory(destDir);

        foreach (var file in Directory.GetFiles(srcDir))
        {
            var fileName = Path.GetFileName(file);
            var destPath = Path.Combine(destDir, fileName);
            await CopyFileAsync(file, destPath);
        }

        foreach (var subDir in Directory.GetDirectories(srcDir))
        {
            var subDirName = Path.GetFileName(subDir);
            var destSubDir = Path.Combine(destDir, subDirName);
            await CopyDirectoryAsync(subDir, destSubDir);
        }
    }

    /// <summary>异步复制单个文件（File.Copy 无异步 API，使用 FileStream + CopyToAsync）</summary>
    public static async Task CopyFileAsync(string sourceFile, string destPath, bool overwrite = true)
    {
        const int bufferSize = 81920;
        using var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var target = new FileStream(destPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await source.CopyToAsync(target);
    }

    /// <summary>递归删除目录（安全）</summary>
    public static Task DeleteDirectoryAsync(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>文件是否存在</summary>
    public static bool FileExists(string path) => File.Exists(path);

    /// <summary>目录是否存在</summary>
    public static bool DirectoryExists(string path) => Directory.Exists(path);

    /// <summary>判断是否为文本文件（基于扩展名）</summary>
    public static bool IsTextFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".story" or ".cs" or ".csproj" or ".json" or ".xml" or ".md" or ".txt"
            or ".axaml" or ".xaml" or ".slnx" or ".props" or ".plist" or ".manifest"
            or ".xib" or ".temp" or ".json.template";
    }
}
