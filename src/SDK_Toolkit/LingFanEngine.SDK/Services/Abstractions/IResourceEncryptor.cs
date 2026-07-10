using System.Threading.Tasks;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>资源加密服务</summary>
public interface IResourceEncryptor
{
    /// <summary>加密文件</summary>
    Task EncryptFileAsync(string inputPath, string outputPath, byte[] key);

    /// <summary>解密文件</summary>
    Task<byte[]> DecryptFileAsync(string inputPath, byte[] key);

    /// <summary>加密目录下指定扩展名的文件</summary>
    Task EncryptDirectoryAsync(string inputDir, string outputDir, byte[] key, string[] extensions);

    /// <summary>检测文件是否已加密（魔数头）</summary>
    bool IsEncrypted(string filePath);
}
