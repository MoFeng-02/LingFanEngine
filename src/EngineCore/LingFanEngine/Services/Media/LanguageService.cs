using System.Text.Json;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Media;

/// <summary>
/// 语言服务——多语言文本翻译
/// <para>主语言文本即为最终文本，翻译文件只做"原文→译文"映射。</para>
/// <para>文件结构：Lang/{语言代码}.json 或 Lang/{场景子目录}/{语言代码}.json</para>
/// </summary>
public class LanguageService
{
    private readonly IStateContainer _state;
    private readonly Dictionary<string, string> _translations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 当前语言代码，如 "zh-CN", "en-US"
    /// </summary>
    public string CurrentLanguage { get; private set; } = "zh-CN";

    /// <summary>
    /// 已加载的翻译条目数
    /// </summary>
    public int LoadedEntryCount => _translations.Count;

    public LanguageService(IStateContainer state)
    {
        _state = state;
    }

    /// <summary>
    /// 切换语言（不自动加载翻译，需手动调用 LoadFromFile）
    /// </summary>
    public void SetLanguage(string languageCode)
    {
        CurrentLanguage = languageCode;
        _state.Set("__current_language", languageCode);
    }

    /// <summary>
    /// 加载全局翻译文件
    /// <para>Lang/{languageCode}.json</para>
    /// </summary>
    public int LoadGlobalTranslation(string languageCode)
    {
        return LoadFromFile($"Lang/{languageCode}.json");
    }

    /// <summary>
    /// 加载场景翻译文件
    /// <para>Lang/{sceneDirectory}/{languageCode}.json</para>
    /// </summary>
    public int LoadSceneTranslation(string sceneDirectory, string languageCode)
    {
        return LoadFromFile($"Lang/{sceneDirectory}/{languageCode}.json");
    }

    /// <summary>
    /// 加载翻译 JSON 文件（原文→译文映射）
    /// </summary>
    public int LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return 0;
        try
        {
            var json = File.ReadAllText(filePath);
            return LoadFromJson(json);
        }
        catch { return 0; }
    }

    public int LoadFromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var count = 0;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    _translations[prop.Name] = prop.Value.GetString()!;
                    count++;
                }
                else if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    count += FlattenObject(prop.Name, prop.Value);
                }
            }
            _state.Set("__lang_entries", _translations.Count);
            return count;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 翻译文本
    /// <para>主语言（zh-CN）时返回原文；外语时查找翻译映射。</para>
    /// </summary>
    public string? Translate(string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText)) return null;
        if (CurrentLanguage == "zh-CN") return null;
        return _translations.TryGetValue(sourceText, out var value) ? value : null;
    }

    public void Clear()
    {
        _translations.Clear();
    }

    private int FlattenObject(string prefix, JsonElement element)
    {
        var count = 0;
        foreach (var prop in element.EnumerateObject())
        {
            var key = $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                _translations[key] = prop.Value.GetString()!;
                count++;
            }
            else if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                count += FlattenObject(key, prop.Value);
            }
        }
        return count;
    }
}
