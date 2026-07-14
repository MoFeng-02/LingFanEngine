using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Resources;

/// <summary>
/// 加密资源包加载器
/// <para>支持挂载 AES-256-GCM 加密的 ZIP 包（.lfpack 格式），运行时以只读方式读取包内文件。</para>
/// <para>.lfpack 格式：LFPK魔数(4) + manifest长度(4, big-endian) + manifest JSON + LFEN加密的ZIP数据</para>
/// <para>LFEN 加密格式（与 SDK AesEncryptor 一致）：LFEN魔数(4) + 版本(1) + nonce(12) + tag(16) + ciphertext</para>
/// <para>文件→包映射精确追踪，Unmount 时只移除对应包的文件。</para>
/// </summary>
public class PackLoader
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _fileToPack = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PackManifest> _mountedPacks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>.lfpack 文件魔数 "LFPK"</summary>
    public static readonly byte[] PackMagic = [0x4C, 0x46, 0x50, 0x4B];

    /// <summary>LFEN 加密数据魔数 "LFEN"（与 SDK AesEncryptor.Magic 一致）</summary>
    public static readonly byte[] EncryptedMagic = [0x4C, 0x46, 0x45, 0x4E];

    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>
    /// 挂载加密资源包
    /// <para>.lfpack 格式：LFPK魔数(4) + manifest长度(4, big-endian) + manifest JSON + LFEN加密的ZIP数据</para>
    /// <para>兼容旧格式：无 LFPK 魔数时尝试旧 AES-CBC 格式（需 key+iv）。</para>
    /// </summary>
    /// <param name="packPath">加密包文件路径</param>
    /// <param name="key">AES-256 密钥（32 字节）</param>
    /// <param name="iv">AES 初始向量（16 字节，仅旧格式需要，新格式忽略）</param>
    /// <param name="ct">取消令牌</param>
    public async ValueTask MountAsync(string packPath, byte[] key, byte[]? iv = null, CancellationToken ct = default)
    {
        var packId = Path.GetFileNameWithoutExtension(packPath);
        var rawData = await File.ReadAllBytesAsync(packPath, ct);

        PackManifest? manifest = null;
        byte[] zipData;

        // 检查 .lfpack 格式（LFPK 魔数头）
        if (rawData.Length >= PackMagic.Length && rawData.AsSpan(0, PackMagic.Length).SequenceEqual(PackMagic))
        {
            // 新格式：LFPK(4) + manifest长度(4) + manifest JSON + LFEN加密的ZIP
            var manifestLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(
                rawData.AsSpan(PackMagic.Length));
            if (manifestLen < 0 || manifestLen > rawData.Length - PackMagic.Length - 4)
                throw new FormatException($"无效的 manifest 长度: {manifestLen}");

            var manifestJson = System.Text.Encoding.UTF8.GetString(
                rawData, PackMagic.Length + 4, manifestLen);

            manifest = JsonSerializer.Deserialize(manifestJson,
                LingFanEngine.Abstractions.Serialization.LfJsonContext.Default.PackManifest);

            var encryptedStart = PackMagic.Length + 4 + manifestLen;

            // 直接用 Span 切片引用 rawData，避免中间 byte[] 分配 + Array.Copy
            zipData = DecryptAesGcm(rawData.AsSpan(encryptedStart), key);
        }
        else
        {
            // 旧格式兼容：AES-CBC 加密的 ZIP（必须提供 iv）
            if (iv == null)
                throw new ArgumentException("旧格式加密包必须提供 IV（AES-CBC 初始向量）", nameof(iv));
            zipData = DecryptAesCbc(rawData, key, iv);
        }

        using var ms = new MemoryStream(zipData);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var fileNames = new List<string>();

        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0) continue;

            using var stream = entry.Open();
            var buf = new byte[entry.Length];
            await stream.ReadExactlyAsync(buf, ct);

            _files[entry.FullName] = buf;
            _fileToPack[entry.FullName] = packId;
            fileNames.Add(entry.FullName);
        }

        if (manifest != null)
        {
            _mountedPacks[packId] = manifest;
        }
        else
        {
            _mountedPacks[packId] = new PackManifest
            {
                Name = packId,
                Files = fileNames
            };
        }
    }

    /// <summary>
    /// 从已挂载的包中读取文件字节
    /// </summary>
    /// <param name="path">包内相对路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>文件字节数据，不存在返回 null</returns>
    public ValueTask<byte[]?> ReadBytesAsync(string path, CancellationToken ct = default) =>
        ValueTask.FromResult(_files.TryGetValue(path, out var data) ? data : null);

    /// <summary>检查文件是否在已挂载的包中存在</summary>
    public bool Exists(string path) => _files.ContainsKey(path);

    /// <summary>
    /// 卸载指定资源包，移除其所有文件
    /// </summary>
    /// <param name="packId">包标识（不含扩展名的文件名）</param>
    public void Unmount(string packId)
    {
        var keysToRemove = _fileToPack
            .Where(kvp => string.Equals(kvp.Value, packId, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _files.TryRemove(key, out _);
            _fileToPack.TryRemove(key, out _);
        }

        _mountedPacks.TryRemove(packId, out _);
    }

    /// <summary>获取已挂载包的清单信息</summary>
    public PackManifest? GetManifest(string packId) =>
        _mountedPacks.TryGetValue(packId, out var manifest) ? manifest : null;

    /// <summary>获取所有已挂载的包标识列表</summary>
    public IEnumerable<string> MountedPacks => _mountedPacks.Keys;

    /// <summary>当前缓存的文件数量</summary>
    public int FileCount => _files.Count;

    /// <summary>当前缓存的包内文件路径列表</summary>
    public IEnumerable<string> PackedFiles => _files.Keys;

    // ========== AES-GCM 解密（与 SDK AesEncryptor 格式一致） ==========

    /// <summary>
    /// AES-256-GCM 解密
    /// <para>数据格式：LFEN魔数(4) + 版本(1) + nonce(12) + tag(16) + ciphertext</para>
    /// <para>必须包含 LFEN 魔数头，否则抛出 FormatException。</para>
    /// </summary>
    private static byte[] DecryptAesGcm(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256 密钥必须为 32 字节", nameof(key));

        // 必须包含 LFEN 魔数 + 版本号 + nonce + tag
        var headerSize = EncryptedMagic.Length + 1 + NonceSize + TagSize;
        if (encrypted.Length < headerSize)
            throw new FormatException($"加密数据过短（{encrypted.Length} < {headerSize}），不是有效的 LFEN 格式");

        // 验证 LFEN 魔数
        if (!encrypted[..EncryptedMagic.Length].SequenceEqual(EncryptedMagic))
            throw new FormatException("加密数据缺少 LFEN 魔数头，不是有效的 LFEN 格式");

        // 验证版本号
        var version = encrypted[EncryptedMagic.Length];
        if (version != 1)
            throw new FormatException($"不支持的 LFEN 加密版本: {version}");

        // 直接用 Span 切片引用原始数据——零拷贝（nonce/tag/ciphertext 共享同一底层 byte[]）
        var headerEnd = EncryptedMagic.Length + 1; // 魔数 + 版本号
        var nonce = encrypted.Slice(headerEnd, NonceSize);
        var tag = encrypted.Slice(headerEnd + NonceSize, TagSize);
        var ciphertext = encrypted[(headerEnd + NonceSize + TagSize)..];

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>
    /// 旧格式 AES-CBC 解密（兼容 BinarySaveService 的 AesEncryption）
    /// </summary>
    private static byte[] DecryptAesCbc(byte[] encrypted, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
    }
}

/// <summary>
/// 加密资源包构建器（开发期工具）
/// <para>将指定目录下的所有文件打包为 .lfpack 格式。</para>
/// <para>.lfpack 格式：LFPK魔数(4) + manifest长度(4, big-endian) + manifest JSON + LFEN加密的ZIP数据</para>
/// </summary>
public static class PackBuilder
{
    /// <summary>
    /// 构建 .lfpack 格式的加密资源包（AES-256-GCM）
    /// </summary>
    /// <param name="sourceDir">源目录</param>
    /// <param name="outputPath">输出 .lfpack 文件路径</param>
    /// <param name="key">AES-256 密钥（32 字节）</param>
    /// <param name="manifest">可选的包清单（null=自动生成）</param>
    public static async Task BuildAsync(
        string sourceDir, string outputPath, byte[] key,
        PackManifest? manifest = null)
    {
        var fileList = new List<string>();

        // 1. 创建 ZIP
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                await fileStream.CopyToAsync(entryStream);
                fileList.Add(relativePath);
            }
        }

        // 2. AES-GCM 加密 ZIP 数据
        ms.Position = 0;
        var zipData = ms.ToArray();
        var encryptedZip = EncryptAesGcm(zipData, key);

        // 3. 构建清单
        var packId = Path.GetFileNameWithoutExtension(outputPath);
        manifest ??= new PackManifest { Name = packId };
        manifest.Files = fileList;
        manifest.CreatedAt = DateTimeOffset.UtcNow;

        var manifestJson = JsonSerializer.Serialize(manifest,
            LingFanEngine.Abstractions.Serialization.LfJsonContext.Default.PackManifest);
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);
        // 4. 组装 .lfpack：LFPK(4) + manifest长度(4, big-endian) + manifest JSON + 加密 ZIP
        using var outputMs = new MemoryStream();
        var manifestLenBuf = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(manifestLenBuf, manifestBytes.Length);
        outputMs.Write(PackLoader.PackMagic);
        outputMs.Write(manifestLenBuf);
        outputMs.Write(manifestBytes);
        outputMs.Write(encryptedZip);

        await File.WriteAllBytesAsync(outputPath, outputMs.ToArray());
    }

    /// <summary>
    /// AES-256-GCM 加密（与 SDK AesEncryptor 格式一致）
    /// <para>输出格式：LFEN魔数(4) + 版本(1) + nonce(12) + tag(16) + ciphertext</para>
    /// </summary>
    private static byte[] EncryptAesGcm(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256 密钥必须为 32 字节", nameof(key));

        const int nonceSize = 12;
        const int tagSize = 16;

        // 一次性分配结果缓冲区：LFEN(4) + 版本(1) + nonce(12) + tag(16) + ciphertext
        var result = new byte[PackLoader.EncryptedMagic.Length + 1 + nonceSize + tagSize + plaintext.Length];

        // 写入头部
        PackLoader.EncryptedMagic.CopyTo(result, 0);
        result[PackLoader.EncryptedMagic.Length] = 1; // version

        // nonce 直接写入 result 的对应位置（避免单独分配 byte[]）
        var nonceSpan = result.AsSpan(PackLoader.EncryptedMagic.Length + 1, nonceSize);
        RandomNumberGenerator.Fill(nonceSpan);

        // ciphertext 直接写入 result 的尾部区域——零中间分配
        var ciphertextStart = PackLoader.EncryptedMagic.Length + 1 + nonceSize + tagSize;
        var ciphertextSpan = result.AsSpan(ciphertextStart);
        var tagSpan = result.AsSpan(PackLoader.EncryptedMagic.Length + 1 + nonceSize, tagSize);

        using var aes = new AesGcm(key, tagSize);
        aes.Encrypt(nonceSpan, plaintext, ciphertextSpan, tagSpan);

        return result;
    }
}
