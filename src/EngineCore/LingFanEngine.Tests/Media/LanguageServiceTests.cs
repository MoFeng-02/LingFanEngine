using FluentAssertions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Media;
using Xunit;

namespace LingFanEngine.Tests.Media;

/// <summary>
/// LanguageService 语言服务测试
/// <para>用 StateContainer 验证 LoadFromJson/对象扁平化/Translate/SetLanguage/LoadFromFile。</para>
/// <para>主语言 zh-CN 时 Translate 返回 null（原文即终稿），需切换到外语才进行映射查找。</para>
/// </summary>
public class LanguageServiceTests : IDisposable
{
    private readonly string _tempFile;
    private readonly StateContainer _state = new();
    private readonly LanguageService _service;

    public LanguageServiceTests()
    {
        _tempFile = Path.GetTempFileName();
        _service = new LanguageService(_state);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    [Fact]
    public void DefaultLanguage_IsZhCn()
    {
        _service.CurrentLanguage.Should().Be("zh-CN");
    }

    [Fact]
    public void SetLanguage_UpdatesCurrentLanguage()
    {
        _service.SetLanguage("en-US");
        _service.CurrentLanguage.Should().Be("en-US");
        _state.Get<string>("__current_language").Should().Be("en-US");
    }

    [Fact]
    public void LoadFromJson_ParsesStringEntries()
    {
        var count = _service.LoadFromJson("{\"hello\":\"你好\",\"world\":\"世界\"}");
        count.Should().Be(2);
        _service.LoadedEntryCount.Should().Be(2);
    }

    [Fact]
    public void LoadFromJson_FlattensNestedObjects()
    {
        var count = _service.LoadFromJson("{\"menu\":{\"start\":\"开始\",\"quit\":\"退出\"}}");
        count.Should().Be(2);

        _service.SetLanguage("en-US");
        _service.Translate("menu.start").Should().Be("开始");
        _service.Translate("menu.quit").Should().Be("退出");
    }

    [Fact]
    public void Translate_WhenZhCn_ReturnsNull()
    {
        _service.LoadFromJson("{\"key\":\"译文\"}");
        // 主语言不翻译
        _service.Translate("key").Should().BeNull();
    }

    [Fact]
    public void Translate_WhenForeignLanguage_ReturnsMapping()
    {
        _service.LoadFromJson("{\"hello\":\"你好\"}");
        _service.SetLanguage("en-US");

        _service.Translate("hello").Should().Be("你好");
    }

    [Fact]
    public void Translate_MissingKey_ReturnsNull()
    {
        _service.SetLanguage("en-US");
        _service.Translate("not_defined").Should().BeNull();
    }

    [Fact]
    public void Translate_EmptyOrNull_ReturnsNull()
    {
        _service.SetLanguage("en-US");
        _service.Translate("").Should().BeNull();
        _service.Translate(null).Should().BeNull();
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _service.LoadFromJson("{\"a\":\"1\",\"b\":\"2\"}");
        _service.Clear();

        _service.LoadedEntryCount.Should().Be(0);
    }

    [Fact]
    public void LoadFromFile_ReadsTranslationFile()
    {
        File.WriteAllText(_tempFile, "{\"title\":\"标题\",\"subtitle\":\"副标题\"}");

        var count = _service.LoadFromFile(_tempFile);

        count.Should().Be(2);
        _service.SetLanguage("en-US");
        _service.Translate("title").Should().Be("标题");
    }

    [Fact]
    public void LoadFromFile_MissingFile_ReturnsZero()
    {
        var count = _service.LoadFromFile(Path.Combine(Path.GetTempPath(), "definitely_missing_xyz.json"));
        count.Should().Be(0);
    }
}
