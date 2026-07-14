using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Resources;

/// <summary>
/// 加密文件读取器——即解即用模式。
/// <para>读取文件时自动检测 LFEN 魔数头：加密则解密后返回明文，不加密则直接返回原始数据。</para>
/// <para>无密钥时（开发期）跳过解密检测，直接读取文件。</para>
/// <para>LFEN 格式：魔数(4) + 版本(1) + nonce(12) + tag(16) + ciphertext</para>
/// </summary>
public class EncryptedFileReader : IEncryptedFileReader
{
    /// <summary>LFEN 魔数 "LFEN"</summary>
    private static readonly byte[] s_lfenMagic = [0x4C, 0x46, 0x45, 0x4E];

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 4 + 1 + NonceSize + TagSize; // 魔数 + 版本 + nonce + tag

    private readonly IEncryptionKeyProvider? _keyProvider;
    private readonly string _tempDir;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="keyProvider">密钥提供者（null = 开发期无加密）</param>
    public EncryptedFileReader(IEncryptionKeyProvider? keyProvider = null)
    {
        _keyProvider = keyProvider;
        _tempDir = Path.Combine(Path.GetTempPath(), "LingFanEngine", "decrypted");
        Directory.CreateDirectory(_tempDir);
    }

    /// <inheritdoc/>
    public async ValueTask<byte[]?> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;

        var data = await File.ReadAllBytesAsync(path, ct);
        return IsLfenEncrypted(data) ? Decrypt(data) : data;
    }

    /// <inheritdoc/>
    public async ValueTask<string?> ReadAllTextAsync(string path, CancellationToken ct = default)
    {
        var bytes = await ReadAllBytesAsync(path, ct);
        return bytes != null ? System.Text.Encoding.UTF8.GetString(bytes) : null;
    }

    /// <inheritdoc/>
    public Stream? OpenRead(string path)
    {
        if (!File.Exists(path)) return null;

        // 快速检测：非加密文件直接返回 FileStream（流式读取，零内存开销）
        if (!IsEncrypted(path))
            return File.OpenRead(path);

        // 加密文件：读取全部字节 → 解密 → 返回 MemoryStream
        var data = File.ReadAllBytes(path);
        var plain = Decrypt(data);
        return new MemoryStream(plain);
    }

    /// <inheritdoc/>
    public (string path, bool isTemp) TryDecryptToFile(string path)
    {
        if (!File.Exists(path)) return (path, false);

        var data = File.ReadAllBytes(path);
        if (!IsLfenEncrypted(data)) return (path, false);

        // 解密到临时文件
        var plain = Decrypt(data);
        var tempPath = Path.Combine(_tempDir, $"{Path.GetFileNameWithoutExtension(path)}_{Guid.NewGuid():N}{Path.GetExtension(path)}");
        File.WriteAllBytes(tempPath, plain);
        return (tempPath, true);
    }

    /// <inheritdoc/>
    public void ReleaseTempFile(string tempPath, bool isTemp)
    {
        if (!isTemp) return;
        try { if (File.Exists(tempPath)) File.Delete(tempPath); }
        catch (Exception ex) { Debug.WriteLine($"[EncryptedFileReader] 释放临时文件失败: {tempPath} — {ex.Message}"); }
    }

    /// <inheritdoc/>
    public bool IsEncrypted(string path)
    {
        if (!File.Exists(path)) return false;
        using var fs = File.OpenRead(path);
        if (fs.Length < HeaderSize) return false;
        Span<byte> header = stackalloc byte[4];
        fs.ReadExactly(header);
        return header.SequenceEqual(s_lfenMagic);
    }

    /// <summary>
    /// 检测字节数据是否为 LFEN 加密格式
    /// </summary>
    private static bool IsLfenEncrypted(byte[] data)
    {
        if (data.Length < HeaderSize) return false;
        return data.AsSpan(0, 4).SequenceEqual(s_lfenMagic);
    }

    /// <summary>
    /// AES-256-GCM 解密 LFEN 格式数据
    /// </summary>
    private byte[] Decrypt(byte[] encrypted)
    {
        var key = _keyProvider?.GetKey();
        if (key == null)
            throw new InvalidOperationException("文件已加密但未注册 IEncryptionKeyProvider——请检查游戏层是否已注入密钥");

        if (key.Length != 32)
            throw new ArgumentException("AES-256 密钥必须为 32 字节");

        // 验证版本号
        var version = encrypted[4];
        if (version != 1)
            throw new FormatException($"不支持的 LFEN 加密版本: {version}");

        // 提取 nonce(12) + tag(16) + ciphertext——Span 切片零拷贝
        var nonce = encrypted.AsSpan(5, NonceSize);
        var tag = encrypted.AsSpan(5 + NonceSize, TagSize);
        var ciphertext = encrypted.AsSpan(5 + NonceSize + TagSize);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
