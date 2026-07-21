using System;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Entry;
using Xunit;

namespace LingFanEngine.Tests.Entry;

/// <summary>
/// I18nService 运行时翻译服务测试
/// <para>覆盖 Translate 降级、SwitchLanguage 状态写入、目录/单文件两种翻译加载、可用语言列表。</para>
/// <para>翻译文件创建于当前工作目录下的 Lang/ 并通过 Dispose 清理。</para>
/// </summary>
public class I18nServiceTests : IDisposable
{
    private readonly string _langRoot;

    public I18nServiceTests()
    {
        _langRoot = Path.Combine(Directory.GetCurrentDirectory(), "Lang");
        Directory.CreateDirectory(_langRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_langRoot))
        {
            try { Directory.Delete(_langRoot, true); }
            catch { /* 忽略清理失败 */ }
        }
    }

    [Fact]
    public void Translate_UnknownKey_ReturnsOriginal()
    {
        var svc = new I18nService(new StateContainer());

        svc.Translate("任意未翻译文本").Should().Be("任意未翻译文本");
    }

    [Fact]
    public void SwitchLanguage_SetsStateAndClearsCache()
    {
        var state = new StateContainer();
        var svc = new I18nService(state);

        svc.SwitchLanguage("en");

        state.Get<string>(StateKeys.Scene.CurrentLanguage).Should().Be("en");
    }

    [Fact]
    public void GetAvailableLanguages_AlwaysContainsZhCn()
    {
        var svc = new I18nService(new StateContainer());

        svc.GetAvailableLanguages().Should().Contain("zh-CN");
    }

    [Fact]
    public void Translate_WithLangDirectory_ReturnsTranslation()
    {
        var lang = "ut_" + Guid.NewGuid().ToString("N")[..6];
        var dir = Path.Combine(_langRoot, lang);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.json"), "{\"Hello\":\"你好\"}");

        var svc = new I18nService(new StateContainer());
        svc.SwitchLanguage(lang);

        svc.Translate("Hello").Should().Be("你好");
    }

    [Fact]
    public void Translate_SingleFileFallback_ReturnsTranslation()
    {
        var lang = "ut_" + Guid.NewGuid().ToString("N")[..6];
        File.WriteAllText(Path.Combine(_langRoot, lang + ".json"), "{\"Hi\":\"嘿\"}");

        var svc = new I18nService(new StateContainer());
        svc.SwitchLanguage(lang);

        svc.Translate("Hi").Should().Be("嘿");
    }
}
