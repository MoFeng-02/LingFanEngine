using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Resources;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace LingFanEngine.Tests.Resources;

/// <summary>
/// EncryptedFileReader 测试：LFEN(AES-256-GCM) 加密文件的读取/解密往返、明文直读、加密探测与临时文件。
/// <para>EncryptedFileReader 仅有解密能力，故测试中用手工 AES-GCM 加密生成 LFEN 文件（与 PackLoader 同格式）。</para>
/// </summary>
public class EncryptedFileReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly byte[] _key;

    public EncryptedFileReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lf_efr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _key = new byte[32];
        RandomNumberGenerator.Fill(_key);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// 生成 LFEN 格式（magic + version + nonce + tag + ciphertext）的加密字节。
    /// </summary>
    private byte[] EncryptToLfen(string plainText, byte[] key)
    {
        var plain = Encoding.UTF8.GetBytes(plainText);
        var result = new byte[4 + 1 + 12 + 16 + plain.Length];
        // 魔数 "LFEN"
        result[0] = 0x4C; result[1] = 0x46; result[2] = 0x45; result[3] = 0x4E;
        result[4] = 1; // version
        var nonce = result.AsSpan(5, 12);
        RandomNumberGenerator.Fill(nonce);
        var ciphertext = result.AsSpan(5 + 12 + 16);
        var tag = result.AsSpan(5 + 12, 16);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, ciphertext, tag);
        return result;
    }

    private sealed class KeyProvider : IEncryptionKeyProvider
    {
        private readonly byte[] _key;
        public KeyProvider(byte[] key) => _key = key;
        public byte[]? GetKey() => _key;
    }

    [Fact]
    public async Task ReadAllBytesAsync_PlaintextFile_ReturnsRawBytes()
    {
        var path = Path.Combine(_tempDir, "plain.txt");
        var content = Encoding.UTF8.GetBytes("plain text content");
        File.WriteAllBytes(path, content);

        var reader = new EncryptedFileReader(); // 无密钥
        var bytes = await reader.ReadAllBytesAsync(path);

        bytes.Should().NotBeNull();
        bytes.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task ReadAllBytesAsync_EncryptedFile_Decrypts()
    {
        var path = Path.Combine(_tempDir, "secret.lf");
        var plain = "这是一个加密的机密内容";
        File.WriteAllBytes(path, EncryptToLfen(plain, _key));

        var reader = new EncryptedFileReader(new KeyProvider(_key));
        var bytes = await reader.ReadAllBytesAsync(path);

        bytes.Should().NotBeNull();
        Encoding.UTF8.GetString(bytes!).Should().Be(plain);
    }

    [Fact]
    public async Task ReadAllTextAsync_EncryptedFile_ReturnsPlaintext()
    {
        var path = Path.Combine(_tempDir, "secret.lf");
        var plain = "decrypted text";
        File.WriteAllBytes(path, EncryptToLfen(plain, _key));

        var reader = new EncryptedFileReader(new KeyProvider(_key));
        var text = await reader.ReadAllTextAsync(path);

        text.Should().Be(plain);
    }

    [Fact]
    public async Task ReadAllBytesAsync_NonExistentFile_ReturnsNull()
    {
        var reader = new EncryptedFileReader(new KeyProvider(_key));
        var bytes = await reader.ReadAllBytesAsync(Path.Combine(_tempDir, "nope.lf"));
        bytes.Should().BeNull();
    }

    [Fact]
    public void IsEncrypted_DetectsEncryptedVsPlaintext()
    {
        var encPath = Path.Combine(_tempDir, "enc.lf");
        File.WriteAllBytes(encPath, EncryptToLfen("x", _key));
        var plainPath = Path.Combine(_tempDir, "plain.txt");
        File.WriteAllBytes(plainPath, Encoding.UTF8.GetBytes("x"));

        var reader = new EncryptedFileReader(new KeyProvider(_key));
        reader.IsEncrypted(encPath).Should().BeTrue();
        reader.IsEncrypted(plainPath).Should().BeFalse();
        reader.IsEncrypted(Path.Combine(_tempDir, "missing")).Should().BeFalse();
    }

    [Fact]
    public void OpenRead_Plaintext_ReturnsFileStream()
    {
        var path = Path.Combine(_tempDir, "plain.txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("stream"));

        var reader = new EncryptedFileReader();
        using var stream = reader.OpenRead(path);
        stream.Should().NotBeNull();
        using var ms = new MemoryStream();
        stream!.CopyTo(ms);
        Encoding.UTF8.GetString(ms.ToArray()).Should().Be("stream");
    }

    [Fact]
    public void TryDecryptToFile_Plaintext_ReturnsOriginalPath()
    {
        var path = Path.Combine(_tempDir, "plain.txt");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("plain"));

        var reader = new EncryptedFileReader(new KeyProvider(_key));
        var (outPath, isTemp) = reader.TryDecryptToFile(path);

        isTemp.Should().BeFalse();
        outPath.Should().Be(path);
    }

    [Fact]
    public void TryDecryptToFile_Encrypted_ReturnsTempFile_And_ReleaseDeletes()
    {
        var path = Path.Combine(_tempDir, "enc.lf");
        var plain = "temp file body";
        File.WriteAllBytes(path, EncryptToLfen(plain, _key));

        var reader = new EncryptedFileReader(new KeyProvider(_key));
        var (outPath, isTemp) = reader.TryDecryptToFile(path);

        isTemp.Should().BeTrue();
        File.Exists(outPath).Should().BeTrue();
        Encoding.UTF8.GetString(File.ReadAllBytes(outPath)).Should().Be(plain);

        reader.ReleaseTempFile(outPath, isTemp);
        File.Exists(outPath).Should().BeFalse();
    }

    [Fact]
    public async Task EncryptedFile_WithoutKeyProvider_Throws()
    {
        var path = Path.Combine(_tempDir, "enc.lf");
        File.WriteAllBytes(path, EncryptToLfen("x", _key));

        var reader = new EncryptedFileReader(); // 无密钥
        var act = async () => await reader.ReadAllBytesAsync(path);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
