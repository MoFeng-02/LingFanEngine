using System.Security.Cryptography;
using LingFanEngine.Abstractions.Interfaces.Saves;

namespace LingFanEngine.Services.Saves;

/// <summary>
/// 默认加密接口实现（AES-256-GCM，带认证）
/// <para>密文格式 LFSV：魔数(4) + 版本(1) + nonce(12) + tag(16) + 密文。
/// 每次加密生成随机 nonce——修复旧实现 CBC + 固定 IV 的确定性加密缺陷
/// （相同明文前缀产生相同密文前缀，且 IV 与密钥存在确定性关系），
/// 同时 GCM 认证标签使存档篡改可检测（旧 CBC+PKCS7 无完整性保护）。</para>
/// <para>解密兼容：数据不带 LFSV 头时走旧 CBC 路径（存量存档零破坏）。</para>
/// <para>开发者可自行替换为其他加密逻辑。</para>
/// </summary>
public class AesEncryption : IEncryption
{
    /// <summary>LFSV 魔数（"LFSV" = LingFan Save V1）</summary>
    private static readonly byte[] s_lfsvMagic = [0x4C, 0x46, 0x53, 0x56];

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 4 + 1 + NonceSize + TagSize; // 魔数 + 版本 + nonce + tag

    private readonly byte[] _key;
    private readonly byte[] _iv;

    /// <summary>
    /// 使用密钥和IV初始化
    /// </summary>
    /// <param name="key">密钥（32字节）</param>
    /// <param name="iv">初始向量（16字节）——仅旧 CBC 兼容路径使用；GCM 路径每次加密生成随机 nonce，此参数不参与</param>
    public AesEncryption(byte[] key, byte[] iv)
    {
        if (key.Length != 32)
            throw new ArgumentException("Key must be 32 bytes for AES-256", nameof(key));
        if (iv.Length != 16)
            throw new ArgumentException("IV must be 16 bytes for AES", nameof(iv));

        _key = key;
        _iv = iv;
    }

    /// <inheritdoc/>
    public byte[] Encrypt(byte[] data)
    {
        // AES-256-GCM：随机 nonce + 认证标签，输出 LFSV 格式
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[data.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, data, ciphertext, tag);
        }

        var output = new byte[HeaderSize + ciphertext.Length];
        s_lfsvMagic.CopyTo(output, 0);
        output[4] = 1; // 版本
        nonce.CopyTo(output, 5);
        tag.CopyTo(output, 5 + NonceSize);
        ciphertext.AsSpan().CopyTo(output.AsSpan(HeaderSize));
        return output;
    }

    /// <inheritdoc/>
    public byte[] Decrypt(byte[] data)
    {
        if (IsLfsvEncrypted(data))
            return DecryptLfsv(data);

        // 旧格式（CBC + 固定 IV）——存量存档兼容路径
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>检测数据是否为 LFSV 加密格式</summary>
    private static bool IsLfsvEncrypted(byte[] data)
    {
        if (data.Length < HeaderSize) return false;
        return data.AsSpan(0, 4).SequenceEqual(s_lfsvMagic);
    }

    /// <summary>AES-256-GCM 解密 LFSV 格式数据</summary>
    private byte[] DecryptLfsv(byte[] encrypted)
    {
        var version = encrypted[4];
        if (version != 1)
            throw new FormatException($"不支持的 LFSV 加密版本: {version}");

        var nonce = encrypted.AsSpan(5, NonceSize);
        var tag = encrypted.AsSpan(5 + NonceSize, TagSize);
        var ciphertext = encrypted.AsSpan(HeaderSize);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
