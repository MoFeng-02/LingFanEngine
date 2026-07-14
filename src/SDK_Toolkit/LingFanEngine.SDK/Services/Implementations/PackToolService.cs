using System.IO.Compression;
using System.Text.Json;
using LingFanEngine.SDK.Cryptography;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 资源打包服务实现
/// <para>.lfpack 格式：LFPK魔数(4) + manifest长度(4, big-endian) + manifest JSON + LFEN加密的ZIP数据</para>
/// <para>加密使用 AES-256-GCM（与 SDK AesEncryptor 格式一致）。</para>
/// </summary>
public class PackToolService : IPackToolService
{
    /// <summary>.lfpack 文件魔数 "LFPK"</summary>
    private static readonly byte[] s_packMagic = [0x4C, 0x46, 0x50, 0x4B];

    /// <inheritdoc/>
    public async Task<PackResult> PackAsync(
        string sourceDir,
        string outputPath,
        byte[] key,
        PackManifestInfo? manifest = null,
        IProgress<string>? progress = null)
    {
        var logs = new List<string>();
        var result = new PackResult { Logs = logs };

        void Log(string msg)
        {
            logs.Add(msg);
            progress?.Report(msg);
        }

        try
        {
            Log($"开始打包: {sourceDir} → {outputPath}");

            if (!Directory.Exists(sourceDir))
            {
                result.Success = false;
                result.ErrorMessage = $"源目录不存在: {sourceDir}";
                return result;
            }

            var fileList = new List<string>();

            // 1. 创建 ZIP
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(file);
                    await fileStream.CopyToAsync(entryStream);
                    fileList.Add(relativePath);
                }
            }

            Log($"压缩完成，{fileList.Count} 个文件");

            // 2. AES-GCM 加密 ZIP 数据
            ms.Position = 0;
            var zipData = ms.ToArray();
            var encryptedZip = AesEncryptor.Encrypt(zipData, key);

            // 3. 构建清单
            var packId = Path.GetFileNameWithoutExtension(outputPath);
            manifest ??= new PackManifestInfo { Name = packId };
            manifest.Files = fileList;
            manifest.CreatedAt = DateTimeOffset.UtcNow;

            // 4. 序列化清单（使用 System.Text.Json，AOT 友好需手动配置）
            var manifestJson = JsonSerializer.Serialize(manifest, SdkJsonContext.Default.PackManifestInfo);
            var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);

            // 5. 组装 .lfpack
            using var outputMs = new MemoryStream();
            var manifestLenBuf = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(manifestLenBuf, manifestBytes.Length);
            outputMs.Write(s_packMagic);
            outputMs.Write(manifestLenBuf);
            outputMs.Write(manifestBytes);
            outputMs.Write(encryptedZip);

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllBytesAsync(outputPath, outputMs.ToArray());

            result.Success = true;
            result.OutputPath = outputPath;
            result.FileCount = fileList.Count;
            Log($"打包完成: {outputPath}（{fileList.Count} 文件）");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Log($"打包失败: {ex.Message}");
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<PackManifestInfo?> ListAsync(string packPath)
    {
        if (!File.Exists(packPath))
            return null;

        var rawData = await File.ReadAllBytesAsync(packPath);

        // 检查 LFPK 魔数
        if (rawData.Length < s_packMagic.Length ||
            !rawData.AsSpan(0, s_packMagic.Length).SequenceEqual(s_packMagic))
            return null;

        var manifestLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(
            rawData.AsSpan(s_packMagic.Length));
        if (manifestLen < 0 || manifestLen > rawData.Length - s_packMagic.Length - 4)
            return null;

        var manifestJson = System.Text.Encoding.UTF8.GetString(
            rawData, s_packMagic.Length + 4, manifestLen);

        return JsonSerializer.Deserialize(manifestJson, SdkJsonContext.Default.PackManifestInfo);
    }

    /// <inheritdoc/>
    public async Task<PackResult> UnpackAsync(
        string packPath,
        string outputDir,
        byte[] key,
        IProgress<string>? progress = null)
    {
        var logs = new List<string>();
        var result = new PackResult { Logs = logs };

        void Log(string msg)
        {
            logs.Add(msg);
            progress?.Report(msg);
        }

        try
        {
            Log($"开始解包: {packPath} → {outputDir}");

            var rawData = await File.ReadAllBytesAsync(packPath);

            // 检查 LFPK 魔数
            if (rawData.Length < s_packMagic.Length ||
                !rawData.AsSpan(0, s_packMagic.Length).SequenceEqual(s_packMagic))
            {
                result.Success = false;
                result.ErrorMessage = "不是有效的 .lfpack 文件（缺少 LFPK 魔数）";
                return result;
            }

            var manifestLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(
                rawData.AsSpan(s_packMagic.Length));
            if (manifestLen < 0 || manifestLen > rawData.Length - s_packMagic.Length - 4)
            {
                result.Success = false;
                result.ErrorMessage = $"无效的 manifest 长度: {manifestLen}";
                return result;
            }

            var manifestJson = System.Text.Encoding.UTF8.GetString(
                rawData, s_packMagic.Length + 4, manifestLen);

            var manifest = JsonSerializer.Deserialize(manifestJson, SdkJsonContext.Default.PackManifestInfo);

            var encryptedStart = s_packMagic.Length + 4 + manifestLen;

            // 直接用 Span 切片引用 rawData，避免中间 byte[] 分配 + Array.Copy
            var zipData = AesEncryptor.Decrypt(rawData.AsSpan(encryptedStart), key);

            // 解压到目录
            using var ms = new MemoryStream(zipData);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            Directory.CreateDirectory(outputDir);
            var fileCount = 0;

            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0) continue;

                var outputPath = Path.GetFullPath(Path.Combine(outputDir, entry.FullName));
                var fullOutputDir = Path.GetFullPath(outputDir);
                if (!outputPath.StartsWith(fullOutputDir, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"跳过可疑路径: {entry.FullName}");
                    continue;
                }

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using var entryStream = entry.Open();
                using var fileStream = File.Create(outputPath);
                await entryStream.CopyToAsync(fileStream);
                fileCount++;
            }

            result.Success = true;
            result.OutputPath = outputDir;
            result.FileCount = fileCount;
            Log($"解包完成: {outputDir}（{fileCount} 文件）");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            Log($"解包失败: {ex.Message}");
        }

        return result;
    }
}
