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
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string slotId)
    {
        var filePath = GetFilePath(slotId);
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<SaveSlotInfo>> GetAllSaveSlotsAsync()
    {
        var slots = new List<SaveSlotInfo>();

        if (!Directory.Exists(_saveDirectory))
            return slots;

        foreach (var file in Directory.GetFiles(_saveDirectory, "*.sav"))
        {
            try
            {
                var encryptedData = await File.ReadAllBytesAsync(file);
                var decryptedData = _encryption.Decrypt(encryptedData);
                var json = Encoding.UTF8.GetString(decryptedData);
                var data = JsonSerializer.Deserialize(json, LfJsonContext.Default.SaveData);

                if (data != null)
                {
                    slots.Add(new SaveSlotInfo
                    {
                        SlotId = Path.GetFileNameWithoutExtension(file),
                        Name = data.Name,
                        CreateTime = data.CreateTime,
                        UpdateTime = data.UpdateTime,
                        Thumbnail = data.Thumbnail,
                        GameVersion = data.GameVersion
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveService] Skipping corrupted save file '{file}': {ex.Message}");
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
}
