using FluentAssertions;
using LingFanEngine.Services.Saves;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace LingFanEngine.Tests.Saves;

public class AesEncryptionTests
{
    private readonly byte[] _key;
    private readonly byte[] _iv;
    private readonly AesEncryption _encryption;

    public AesEncryptionTests()
    {
        _key = new byte[32];
        _iv = new byte[16];
        RandomNumberGenerator.Fill(_key);
        RandomNumberGenerator.Fill(_iv);
        _encryption = new AesEncryption(_key, _iv);
    }

    [Fact]
    public void Encrypt_Decrypt_RoundTrip()
    {
        var original = Encoding.UTF8.GetBytes("Hello, World!");
        var encrypted = _encryption.Encrypt(original);
        var decrypted = _encryption.Decrypt(encrypted);

        decrypted.Should().NotBeSameAs(original);
        decrypted.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Encrypt_OutputsDifferentData()
    {
        var original = Encoding.UTF8.GetBytes("Hello, World!");
        var encrypted = _encryption.Encrypt(original);

        encrypted.Should().NotBeSameAs(original);
        encrypted.Should().NotBeEquivalentTo(original);
    }

    [Fact]
    public void Decrypt_InvalidData_ThrowsException()
    {
        var invalidData = new byte[] { 1, 2, 3, 4, 5 };
        Assert.ThrowsAny<Exception>(() => _encryption.Decrypt(invalidData));
    }

    [Fact]
    public void EmptyData_RoundTrip()
    {
        var original = Array.Empty<byte>();
        var encrypted = _encryption.Encrypt(original);
        var decrypted = _encryption.Decrypt(encrypted);

        decrypted.Should().BeEmpty();
    }

    [Fact]
    public void Encrypt_UsesRandomNonce_SamePlaintextDifferentCiphertext()
    {
        // GCM 随机 nonce：相同明文两次加密产生不同密文（修复旧 CBC 固定 IV 的确定性加密）
        var original = Encoding.UTF8.GetBytes("Hello, World!");
        var encrypted1 = _encryption.Encrypt(original);
        var encrypted2 = _encryption.Encrypt(original);

        encrypted1.Should().NotBeEquivalentTo(encrypted2);

        _encryption.Decrypt(encrypted1).Should().BeEquivalentTo(original);
        _encryption.Decrypt(encrypted2).Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Decrypt_LegacyCbcFormat_Succeeds()
    {
        // 旧 CBC 存档兼容：不带 LFSV 头的密文（CBC + 固定 IV，升级前格式）仍可解密
        var original = Encoding.UTF8.GetBytes("legacy save data");
        var encrypted = EncryptLegacyCbc(original, _key, _iv);

        var decrypted = _encryption.Decrypt(encrypted);

        decrypted.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        // GCM 认证标签：密文被篡改时解密失败（旧 CBC+PKCS7 无完整性保护，可被静默篡改）
        var original = Encoding.UTF8.GetBytes("integrity check");
        var encrypted = _encryption.Encrypt(original);
        encrypted[^1] ^= 0xFF; // 篡改最后一字节

        var act = () => _encryption.Decrypt(encrypted);
        act.Should().Throw<CryptographicException>();
    }

    /// <summary>用升级前的 CBC + 固定 IV 方式加密（模拟存量旧存档）</summary>
    private static byte[] EncryptLegacyCbc(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(data, 0, data.Length);
    }

    [Fact]
    public void LargeData_RoundTrip()
    {
        var original = new byte[10000];
        RandomNumberGenerator.Fill(original);

        var encrypted = _encryption.Encrypt(original);
        var decrypted = _encryption.Decrypt(encrypted);

        decrypted.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void AesEncryption_InvalidKeySize_Throws()
    {
        var invalidKey = new byte[16];
        var validIv = new byte[16];
        Assert.Throws<ArgumentException>(() => new AesEncryption(invalidKey, validIv));
    }

    [Fact]
    public void AesEncryption_InvalidIvSize_Throws()
    {
        var validKey = new byte[32];
        var invalidIv = new byte[8];
        Assert.Throws<ArgumentException>(() => new AesEncryption(validKey, invalidIv));
    }
}