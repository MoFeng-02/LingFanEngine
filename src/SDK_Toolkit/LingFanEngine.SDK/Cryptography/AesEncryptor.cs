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
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"密钥长度必须为 {KeySize} 字节", nameof(key));

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // 组装结果
        var result = new byte[Magic.Length + 1 + NonceSize + TagSize + ciphertext.Length];
        var offset = 0;

        Magic.CopyTo(result, offset); offset += Magic.Length;
        result[offset++] = Version;
        nonce.CopyTo(result, offset); offset += NonceSize;
        tag.CopyTo(result, offset); offset += TagSize;
        ciphertext.CopyTo(result, offset);

        return result;
    }

    /// <summary>
    /// 解密：从 魔数(4) + 版本(1) + nonce(12) + tag(16) + ciphertext 还原明文
    /// </summary>
    public static byte[] Decrypt(byte[] encrypted, byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"密钥长度必须为 {KeySize} 字节", nameof(key));

        if (!IsEncrypted(encrypted))
            throw new FormatException("数据不是有效的 LFEN 加密格式");

        var version = encrypted[Magic.Length];
        if (version != Version)
            throw new FormatException($"不支持的加密版本: {version}");

        var offset = Magic.Length + 1; // 跳过魔数+版本
        var nonce = new byte[NonceSize];
        Array.Copy(encrypted, offset, nonce, 0, NonceSize);
        offset += NonceSize;

        var tag = new byte[TagSize];
        Array.Copy(encrypted, offset, tag, 0, TagSize);
        offset += TagSize;

        var ciphertext = new byte[encrypted.Length - offset];
        Array.Copy(encrypted, offset, ciphertext, 0, ciphertext.Length);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>检测数据是否为 LFEN 加密格式</summary>
    public static bool IsEncrypted(byte[] data)
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
