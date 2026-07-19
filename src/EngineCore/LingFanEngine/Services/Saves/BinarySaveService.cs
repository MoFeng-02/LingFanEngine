using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Abstractions.Serialization;

namespace LingFanEngine.Services.Saves;

/// <summary>
/// 二进制加密存档服务实现
/// <para>使用 JSON 序列化（JsonAOT）+ AES 加密存储。</para>
/// <para>Phase 36: 存档时同时写入 .meta 轻量索引文件（明文 JSON），枚举时只读 .meta，不解密完整存档。</para>
/// </summary>
public class BinarySaveService : ISaveService
{
    private readonly string _saveDirectory;
    private readonly IEncryption _encryption;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="saveDirectory">存档存储目录</param>
    /// <param name="encryption">加密器（默认使用基于机器信息的 AES）</param>
    public BinarySaveService(string saveDirectory, IEncryption? encryption = null)
    {
        _saveDirectory = saveDirectory;
        _encryption = encryption ?? GenerateDefaultEncryption();
    }

    /// <summary>
    /// 构造函数（使用指定密钥）
    /// </summary>
    /// <param name="saveDirectory">存档存储目录</param>
    /// <param name="key">AES-256 密钥（32字节）</param>
    /// <param name="iv">AES IV（16字节）</param>
    public BinarySaveService(string saveDirectory, byte[] key, byte[] iv)
    {
        _saveDirectory = saveDirectory;
        _encryption = new AesEncryption(key, iv);
    }

    /// <summary>
    /// 生成默认加密器（基于机器信息）
    /// </summary>
    private static AesEncryption GenerateDefaultEncryption()
    {
        var machineKey = $"{Environment.MachineName}_{Environment.UserName}";
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(machineKey));
        var iv = new byte[16];
        Array.Copy(key, iv, 16);
        return new AesEncryption(key, iv);
    }

    /// <inheritdoc/>
    public async Task<SaveData?> LoadAsync(string slotId)
    {
        var filePath = GetFilePath(slotId);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var encryptedData = await File.ReadAllBytesAsync(filePath);
            var decryptedData = _encryption.Decrypt(encryptedData);
            var json = Encoding.UTF8.GetString(decryptedData);
            return JsonSerializer.Deserialize(json, LfJsonContext.Default.SaveData);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveService] LoadAsync failed for slot '{slotId}': {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string slotId, SaveData data)
    {
        Directory.CreateDirectory(_saveDirectory);
        data.UpdateTime = DateTimeOffset.UtcNow;

        var json = JsonSerializer.Serialize(data, LfJsonContext.Default.SaveData);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var encryptedData = _encryption.Encrypt(jsonBytes);
        await File.WriteAllBytesAsync(GetFilePath(slotId), encryptedData);

        // Phase 36: 写入 .meta 轻量索引文件（明文 JSON，仅含展示信息）
        // 枚举存档列表时只读 .meta，无需解密+反序列化完整存档
        await WriteMetaAsync(slotId, data);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string slotId)
    {
        var filePath = GetFilePath(slotId);
        if (File.Exists(filePath))
            File.Delete(filePath);
        // Phase 36: 同时删除 .meta 文件
        var metaPath = GetMetaFilePath(slotId);
        if (File.Exists(metaPath))
            File.Delete(metaPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SaveSlotInfo>> GetAllSaveSlotsAsync()
    {
        var slots = new List<SaveSlotInfo>();

        if (!Directory.Exists(_saveDirectory))
            return slots;

        // Phase 36: 优先读取 .meta 轻量索引文件（明文 JSON，无需解密）
        var metaFiles = Directory.GetFiles(_saveDirectory, "*.meta");
        var savFiles = Directory.GetFiles(_saveDirectory, "*.sav");
        var metaSlotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 快速路径：从 .meta 文件读取
        foreach (var metaFile in metaFiles)
        {
            try
            {
                var metaJson = await File.ReadAllTextAsync(metaFile);
                var info = JsonSerializer.Deserialize(metaJson, LfJsonContext.Default.SaveSlotInfo);
                if (info != null)
                {
                    slots.Add(info);
                    metaSlotIds.Add(Path.GetFileNameWithoutExtension(metaFile));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveService] Skipping corrupted meta file '{metaFile}': {ex.Message}");
            }
        }

        // 兼容路径：无 .meta 的旧存档 → 回退到解密+反序列化完整存档
        foreach (var savFile in savFiles)
        {
            var slotId = Path.GetFileNameWithoutExtension(savFile);
            if (metaSlotIds.Contains(slotId))
                continue; // 已有 .meta，跳过

            try
            {
                var encryptedData = await File.ReadAllBytesAsync(savFile);
                var decryptedData = _encryption.Decrypt(encryptedData);
                var json = Encoding.UTF8.GetString(decryptedData);
                var data = JsonSerializer.Deserialize(json, LfJsonContext.Default.SaveData);

                if (data != null)
                {
                    var info = new SaveSlotInfo
                    {
                        SlotId = slotId,
                        Name = data.Name,
                        CreateTime = data.CreateTime,
                        UpdateTime = data.UpdateTime,
                        Thumbnail = data.Thumbnail,
                        GameVersion = data.GameVersion
                    };
                    slots.Add(info);

                    // 补写 .meta 文件，下次不再需要解密
                    await WriteMetaAsync(slotId, data);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveService] Skipping corrupted save file '{savFile}': {ex.Message}");
            }
        }

        return slots.OrderByDescending(s => s.UpdateTime);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string slotId)
    {
        return Task.FromResult(File.Exists(GetFilePath(slotId)));
    }

    /// <summary>
    /// 获取存档文件路径
    /// </summary>
    private string GetFilePath(string slotId)
    {
        var safeSlotId = string.Join("_", slotId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_saveDirectory, $"{safeSlotId}.sav");
    }

    /// <summary>
    /// 获取 .meta 索引文件路径
    /// </summary>
    private string GetMetaFilePath(string slotId)
    {
        var safeSlotId = string.Join("_", slotId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_saveDirectory, $"{safeSlotId}.meta");
    }

    /// <summary>
    /// 写入 .meta 轻量索引文件（明文 JSON，仅含展示信息）
    /// </summary>
    private async Task WriteMetaAsync(string slotId, SaveData data)
    {
        try
        {
            var info = new SaveSlotInfo
            {
                SlotId = slotId,
                Name = data.Name,
                CreateTime = data.CreateTime,
                UpdateTime = data.UpdateTime,
                Thumbnail = data.Thumbnail,
                GameVersion = data.GameVersion
            };
            var metaJson = JsonSerializer.Serialize(info, LfJsonContext.Default.SaveSlotInfo);
            await File.WriteAllTextAsync(GetMetaFilePath(slotId), metaJson);
        }
        catch (Exception ex)
        {
            // .meta 写入失败不影响存档主体
            System.Diagnostics.Debug.WriteLine($"[SaveService] WriteMetaAsync failed for slot '{slotId}': {ex.Message}");
        }
    }
}
