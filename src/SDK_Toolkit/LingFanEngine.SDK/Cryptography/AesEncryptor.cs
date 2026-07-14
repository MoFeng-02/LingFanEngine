using System;
using System.Security.Cryptography;

namespace LingFanEngine.SDK.Cryptography;

/// <summary>
/// AES-256-GCM 加密/解密（AOT 友好）
/// <para>加密格式：魔数(4) + 版本(1) + nonce(12) + tag(16) + ciphertext</para>
/// </summary>
public static class AesEncryptor
{
    /// <summary>魔数 "LFEN" = 0x4C 0x46 0x45 0x4E</summary>
    public static readonly byte[] Magic = [0x4C, 0x46, 0x45, 0x4E];

    /// <summary>版本号</summary>
    public const byte Version = 1;

    /// <summary>Nonce 长度</summary>
    private const int NonceSize = 12;

    /// <summary>Tag 长度</summary>
    private const int TagSize = 16;

    /// <summary>密钥长度（AES-256 = 32 字节）</summary>
    private const int KeySize = 32;

    /// <summary>
    /// 加密：返回 魔数(4) + 版本(1) + nonce(12) + tag(16) + ciphertext
    /// <para>一次性分配结果缓冲区，nonce/ciphertext/tag 直接写入对应位置——零中间分配。</para>
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"密钥长度必须为 {KeySize} 字节", nameof(key));

        // 一次性分配结果缓冲区
        var result = new byte[Magic.Length + 1 + NonceSize + TagSize + plaintext.Length];

        // 写入头部
        Magic.CopyTo(result, 0);
        result[Magic.Length] = Version;

        // nonce 直接写入 result（避免单独 byte[] 分配）
        var nonceSpan = result.AsSpan(Magic.Length + 1, NonceSize);
        RandomNumberGenerator.Fill(nonceSpan);

        // ciphertext + tag 直接写入 result 的尾部区域——零中间分配
        var tagSpan = result.AsSpan(Magic.Length + 1 + NonceSize, TagSize);
        var ciphertextSpan = result.AsSpan(Magic.Length + 1 + NonceSize + TagSize);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonceSpan, plaintext, ciphertextSpan, tagSpan);

        return result;
    }

    /// <summary>
    /// 解密：从 魔数(4) + 版本(1) + nonce(12) + tag(16) + ciphertext 还原明文
    /// <para>使用 Span 切片直接引用输入数据——零拷贝 nonce/tag/ciphertext。</para>
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> encrypted, ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"密钥长度必须为 {KeySize} 字节", nameof(key));

        if (!IsEncrypted(encrypted))
            throw new FormatException("数据不是有效的 LFEN 加密格式");

        var version = encrypted[Magic.Length];
        if (version != Version)
            throw new FormatException($"不支持的加密版本: {version}");

        // 直接用 Span 切片引用原始数据——零拷贝
        var headerEnd = Magic.Length + 1; // 魔数 + 版本号
        var nonce = encrypted.Slice(headerEnd, NonceSize);
        var tag = encrypted.Slice(headerEnd + NonceSize, TagSize);
        var ciphertext = encrypted[(headerEnd + NonceSize + TagSize)..];

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>检测数据是否为 LFEN 加密格式</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> data)
    {
        if (data.Length < Magic.Length + 1 + NonceSize + TagSize)
            return false;

        for (var i = 0; i < Magic.Length; i++)
        {
            if (data[i] != Magic[i])
                return false;
        }

        return true;
    }
}
