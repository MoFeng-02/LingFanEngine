using System.Collections.Concurrent;
using System.IO.Compression;
using LingFanEngine.Services.Saves;

namespace LingFanEngine.Services.Resources;

/// <summary>
/// 加密资源包加载器
/// <para>支持挂载 AES 加密的 ZIP 包，运行时以只读方式读取包内文件。</para>
/// </summary>
public class PackLoader
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _mountedPacks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 挂载加密资源包
    /// <para>解密后解压，将所有文件缓存到内存字典中。</para>
    /// </summary>
    /// <param name="packPath">加密包文件路径</param>
    /// <param name="key">AES 密钥（32 字节）</param>
    /// <param name="iv">AES 初始向量（16 字节）</param>
    /// <param name="ct">取消令牌</param>
    public async ValueTask MountAsync(string packPath, byte[] key, byte[] iv, CancellationToken ct = default)
    {
        var packId = Path.GetFileNameWithoutExtension(packPath);
        var encryptedData = await File.ReadAllBytesAsync(packPath, ct);
        var aes = new AesEncryption(key, iv);
        var decryptedZip = aes.Decrypt(encryptedData);

        using var ms = new MemoryStream(decryptedZip);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0) continue;

            using var stream = entry.Open();
            var buf = new byte[entry.Length];
            await stream.ReadExactlyAsync(buf, ct);
            _files[entry.FullName] = buf;
        }

        _mountedPacks[packId] = packPath;
    }

    /// <summary>
    /// 从已挂载的包中读取文件字节
    /// </summary>
    /// <param name="path">包内相对路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>文件字节数据，不存在返回 null</returns>
    public ValueTask<byte[]?> ReadBytesAsync(string path, CancellationToken ct = default) =>
        ValueTask.FromResult(_files.TryGetValue(path, out var data) ? data : null);

    /// <summary>
    /// 检查文件是否在已挂载的包中存在
    /// </summary>
    public bool Exists(string path) => _files.ContainsKey(path);

    /// <summary>
    /// 卸载指定资源包，移除其所有文件
    /// </summary>
    /// <param name="packId">包标识（不含扩展名的文件名）</param>
    public void Unmount(string packId)
    {
        // 移除该包对应的所有文件
        // 注：当前实现未按 packId 追踪每个文件来源，此处清除全部以 packId 开头的键
        // 更精确的实现需要记录文件->包的映射表
        var keysToRemove = _files.Keys
            .Where(k => k.StartsWith(packId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _files.TryRemove(key, out _);
        }

        _mountedPacks.TryRemove(packId, out _);
    }

    /// <summary>
    /// 当前缓存的文件数量
    /// </summary>
    public int FileCount => _files.Count;

    /// <summary>
    /// 当前缓存的包内文件路径列表
    /// </summary>
    public IEnumerable<string> PackedFiles => _files.Keys;
}

/// <summary>
/// 加密资源包构建器（开发期工具）
/// <para>将指定目录下的所有文件打包为 AES 加密的 ZIP 文件。</para>
/// </summary>
public static class PackBuilder
{
    /// <summary>
    /// 构建加密资源包
    /// </summary>
    /// <param name="sourceDir">源目录</param>
    /// <param name="outputPath">输出加密包路径</param>
    /// <param name="key">AES 密钥（32 字节）</param>
    /// <param name="iv">AES 初始向量（16 字节）</param>
    public static async Task BuildAsync(string sourceDir, string outputPath, byte[] key, byte[] iv)
    {
        using var ms = new MemoryStream();

        // 先创建 ZIP 压缩包
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, file);
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream);
            }
        }

        // 再加密整个 ZIP 数据
        ms.Position = 0;
        var zipData = ms.ToArray();
        var aes = new AesEncryption(key, iv);
        var encrypted = aes.Encrypt(zipData);

        await File.WriteAllBytesAsync(outputPath, encrypted);
    }
}
