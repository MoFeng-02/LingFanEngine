using System.Security.Cryptography;
using LingFanEngine.Abstractions.Interfaces.Saves;

namespace LingFanEngine.Services.Saves;

/// <summary>
/// 默认加密接口实现（AES加密）
/// <para>开发者可自行替换为其他加密逻辑</para>
/// </summary>
public class AesEncryption : IEncryption
{
    private readonly byte[] _key;
    private readonly byte[] _iv;

    /// <summary>
    /// 使用密钥和IV初始化
    /// </summary>
    /// <param name="key">密钥（32字节）</param>
    /// <param name="iv">初始向量（16字节）</param>
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
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    /// <inheritdoc/>
    public byte[] Decrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }
}