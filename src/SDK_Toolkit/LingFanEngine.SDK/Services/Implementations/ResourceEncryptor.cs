using System.IO;
using System.Threading.Tasks;
using LingFanEngine.SDK.Cryptography;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>资源加密服务实现</summary>
public class ResourceEncryptor : IResourceEncryptor
{
    /// <inheritdoc/>
    public async Task EncryptFileAsync(string inputPath, string outputPath, byte[] key)
    {
        var plaintext = await File.ReadAllBytesAsync(inputPath);

        // 如果已经是加密文件，直接复制
        if (AesEncryptor.IsEncrypted(plaintext))
        {
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        var encrypted = AesEncryptor.Encrypt(plaintext, key);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(outputPath, encrypted);
    }

    /// <inheritdoc/>
    public async Task<byte[]> DecryptFileAsync(string inputPath, byte[] key)
    {
        var encrypted = await File.ReadAllBytesAsync(inputPath);
        return AesEncryptor.Decrypt(encrypted, key);
    }

    /// <inheritdoc/>
    public async Task EncryptDirectoryAsync(string inputDir, string outputDir, byte[] key, string[] extensions)
    {
        var extSet = new System.Collections.Generic.HashSet<string>(
            System.Array.ConvertAll(extensions, e => e.ToLowerInvariant()));

        // 确保输出目录存在
        Directory.CreateDirectory(outputDir);

        // 加密所有匹配扩展名的文件
        var files = Directory.GetFiles(inputDir, "*.*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!extSet.Contains(ext))
                continue;

            var relativePath = Path.GetRelativePath(inputDir, file);
            var outputPath = Path.Combine(outputDir, relativePath + ".enc");

            await EncryptFileAsync(file, outputPath, key);
        }
    }

    /// <inheritdoc/>
    public bool IsEncrypted(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        // 检查文件扩展名
        if (filePath.EndsWith(".enc"))
            return true;

        // 读取前 4 字节检查魔数
        using var fs = File.OpenRead(filePath);
        if (fs.Length < 4)
            return false;

        var header = new byte[4];
        var read = fs.Read(header, 0, 4);

        for (var i = 0; i < read; i++)
        {
            if (header[i] != AesEncryptor.Magic[i])
                return false;
        }

        return true;
    }
}
