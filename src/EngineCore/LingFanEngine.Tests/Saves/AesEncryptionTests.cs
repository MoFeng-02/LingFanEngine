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