using System.Text.Json;
using System.Text.Json.Serialization;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Saves;

/// <summary>
/// JSON 文件系统配置服务
/// <para>配置存储在 LocalApplicationData/LingFanEngine/config.json。</para>
/// <para>所有 key 建议使用 __ 前缀以便与游戏变量区分。</para>
/// </summary>
public class JsonConfigService : IConfigService
{
    private readonly string _configPath;
    private Dictionary<string, object?> _config = new();

    public JsonConfigService()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _configPath = Path.Combine(baseDir, "LingFanEngine", "config.json");
        Load();
    }

    /// <inheritdoc/>
    public T? Get<T>(string key)
    {
        if (_config.TryGetValue(key, out var val) && val is T typed)
            return typed;
        return default;
    }

    /// <inheritdoc/>
    public void Set<T>(string key, T value)
    {
        _config[key] = value;
        Save();
    }

    /// <inheritdoc/>
    public void Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var raw = JsonSerializer.Deserialize(json,
                    LingFanEngine.Abstractions.Serialization.LfJsonContext.Default.DictionaryStringObject)
                    ?? new Dictionary<string, object?>();

                // 源生成 DictionaryStringObject 将 JSON 数字反序列化为 JsonElement（装箱），
                // 直接存入会导致 Get<int> 等类型化读取永远返回默认值。
                // 统一经 JsonValueConverter 还原为原生类型，确保磁盘重载后的配置可被类型化读取。
                var converter = new JsonValueConverter();
                var converted = new Dictionary<string, object?>(raw.Count);
                foreach (var kvp in raw)
                    converted[kvp.Key] = kvp.Value is JsonElement je ? converter.Convert(je) : kvp.Value;
                _config = converted;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JsonConfigService] Load failed: {ex.Message}");
            _config = new();
        }
    }

    /// <inheritdoc/>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_config,
                LingFanEngine.Abstractions.Serialization.LfJsonContext.Default.DictionaryStringObject);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[JsonConfigService] Save failed: {ex.Message}");
        }
    }
}
