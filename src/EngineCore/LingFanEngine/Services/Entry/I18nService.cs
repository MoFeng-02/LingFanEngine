using System.Collections.Concurrent;
using System.Linq;
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
/// <para>加载策略：语言根目录（Lang/{lang}/）下的所有 .json 文件递归加载——
/// main.json 优先作为全局兜底，其余文件按路径排序加载（后覆盖先）。
/// 内部组织方式完全自由：扁平、子文件夹分类、单个大文件均可。</para>
/// <para>降级：若无 Lang/{lang}/ 目录，则尝试单文件 Lang/{lang}.json。</para>
/// </summary>
public class I18nService : II18nService
{
    private readonly IStateContainer _state;
    private readonly IEncryptedFileReader? _fileReader;
    private readonly ConcurrentDictionary<string, string> _translations = new();
    private string _loadedLang = "";
    private volatile bool _loaded;

    /// <summary>翻译文件根目录下的语言子目录（如 Lang/en-US/）</summary>
    public const string LangRoot = "Lang";

    public I18nService(IStateContainer state, IEncryptedFileReader? fileReader = null)
    {
        _state = state;
        _fileReader = fileReader;
    }

    /// <summary>
    /// 切换语言时调用——清除缓存，下次 Translate() 按需加载
    /// <para>同时写入 StateKeys.Scene.CurrentLanguage 供 UI 层读取。</para>
    /// </summary>
    public void SwitchLanguage(string lang)
    {
        _loadedLang = lang;
        _translations.Clear();
        _loaded = false;
        _state.Set(StateKeys.Scene.CurrentLanguage, lang);
    }

    /// <summary>
    /// 原文→译文翻译
    /// <para>首次调用或语言切换后自动按需加载 Lang/{lang}/ 下的所有翻译文件。</para>
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

    /// <summary>
    /// 获取可用语言列表
    /// <para>扫描 Lang/ 目录下的子目录（如 Lang/en-US/）和独立 JSON 文件（如 Lang/en-US.json）。</para>
    /// <para>始终包含 "zh-CN"（默认语言）。</para>
    /// </summary>
    public IReadOnlyList<string> GetAvailableLanguages()
    {
        var langs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zh-CN" };

        if (Directory.Exists(LangRoot))
        {
            // 子目录形式的语言（Lang/en-US/main.json）
            foreach (var dir in Directory.GetDirectories(LangRoot))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name))
                    langs.Add(name);
            }

            // 独立 JSON 文件形式（Lang/en-US.json）
            foreach (var file in Directory.GetFiles(LangRoot, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(name))
                    langs.Add(name);
            }
        }

        return langs.ToList();
    }

    private void LoadForCurrentLanguage()
    {
        var lang = _loadedLang;
        if (string.IsNullOrEmpty(lang) || lang == "zh-CN" || lang == "zh-Hans")
            return;

        // 目录形式优先：Lang/{lang}/ 递归加载所有 .json 文件
        var langDir = Path.Combine(LangRoot, lang);
        if (Directory.Exists(langDir))
        {
            // 1. 全局兜底——main.json 优先加载（其余文件覆盖它）
            var mainFile = Path.Combine(langDir, "main.json");
            if (File.Exists(mainFile))
                LoadFile(mainFile);

            // 2. 递归加载其余所有 .json 文件（子文件夹/扁平/单文件均支持）
            //    按路径排序保证加载顺序确定——后加载的覆盖先加载的
            foreach (var file in Directory.GetFiles(langDir, "*.json", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(file, mainFile, StringComparison.OrdinalIgnoreCase))
                    LoadFile(file);
            }
        }
        else
        {
            // 降级：单文件形式 Lang/{lang}.json
            var singleFile = Path.Combine(LangRoot, $"{lang}.json");
            if (File.Exists(singleFile))
                LoadFile(singleFile);
        }
    }

    /// <summary>
    /// 加载单个翻译文件——逐条添加/覆盖到 _translations（合并模式）
    /// <para>后加载的文件覆盖先加载的同名键。main.json 最先加载作为全局兜底。</para>
    /// </summary>
    private void LoadFile(string path)
    {
        try
        {
            // Phase 56：通过 IEncryptedFileReader 读取（加密的 .json 翻译文件自动解密）
            string json;
            if (_fileReader != null)
            {
                using var stream = _fileReader.OpenRead(path);
                if (stream == null) return;
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                json = reader.ReadToEnd();
            }
            else
            {
                json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            var dict = JsonSerializer.Deserialize(json,
                LingFanEngine.Abstractions.Serialization.LfJsonContext.Default.DictionaryStringString);
            if (dict == null) return;

            // 逐条合并——后加载的覆盖先加载的
            foreach (var (key, value) in dict)
            {
                _translations[key] = value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[I18nService] Failed to load translation file '{path}': {ex.Message}");
        }
    }
}
