using System.Text.Json;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Entry;

/// <summary>
/// 运行时翻译服务——按需加载翻译文件，纯原文直译
/// <para>启动时只扫描 Lang/ 目录注册可用语言，不加载任何文件。</para>
/// <para>Translate() 首次调用时按需读取 Lang/{lang}/ 并缓存到内存。</para>
/// <para>语言切换时清除缓存，下次调用重新读取。</para>
/// </summary>
public class I18nService : II18nService
{
    private readonly IStateContainer _state;
    private Dictionary<string, string> _translations = new();
    private string _loadedLang = "";
    private bool _loaded;

    /// <summary>翻译文件根目录下的语言子目录（如 Lang/en-US/）</summary>
    public const string LangRoot = "Lang";

    public I18nService(IStateContainer state)
    {
        _state = state;
    }

    /// <summary>
    /// 切换语言时调用——清除缓存，下次 Translate() 按需加载
    /// </summary>
    public void SwitchLanguage(string lang)
    {
        _loadedLang = lang;
        _translations.Clear();
        _loaded = false;
    }

    /// <summary>
    /// 原文→译文翻译
    /// <para>首次调用或语言切换后自动按需加载 Lang/{lang}/ 下的翻译文件。</para>
    /// <para>找不到译文时返回原文。</para>
    /// </summary>
    public string Translate(string original)
    {
        if (string.IsNullOrEmpty(original)) return original;
        if (!_loaded)
        {
            LoadForCurrentLanguage();
            _loaded = true;
        }
        return _translations.TryGetValue(original, out var t) ? t : original;
    }

    private void LoadForCurrentLanguage()
    {
        var lang = _loadedLang;
        if (string.IsNullOrEmpty(lang) || lang == "zh-CN" || lang == "zh-Hans")
            return;

        // 1. 全局翻译（兜底）——Lang/{lang}/main.json 或 Lang/{lang}.json
        var global = Path.Combine(LangRoot, lang, "main.json");
        if (!File.Exists(global))
            global = Path.Combine(LangRoot, $"{lang}.json");
        if (File.Exists(global))
            LoadFile(global);

        // 2. 当前场景级翻译——Lang/{lang}/{sceneName}.json
        var sceneName = _state.Get<string>(StateKeys.Scene.CurrentName);
        if (!string.IsNullOrEmpty(sceneName))
        {
            var scenePath = Path.Combine(LangRoot, lang, $"{sceneName}.json");
            if (File.Exists(scenePath))
                LoadFile(scenePath);
        }
    }

    private void LoadFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            var dict = JsonSerializer.Deserialize(json,
                LingFanEngine.Abstractions.Serialization.LfJsonContext.Default.DictionaryStringString);
            if (dict == null) return;
            _translations = dict;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[I18nService] Failed to load translation file '{path}': {ex.Message}");
        }
    }
}
