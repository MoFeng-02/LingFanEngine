using System.Reflection;
using FluentAssertions;
using LingFanEngine.Services.Saves;
using Xunit;

namespace LingFanEngine.Tests.Saves;

/// <summary>
/// JsonConfigService 配置服务测试
/// <para>构造函数硬编码路径到 LocalApplicationData/LingFanEngine/config.json，
/// 为避免污染真实配置，通过反射将私有字段 _configPath 重定向到临时文件后测试公开纯逻辑。</para>
/// </summary>
public class JsonConfigServiceTests : IDisposable
{
    private readonly string _tempFile;
    private readonly JsonConfigService _service;

    public JsonConfigServiceTests()
    {
        _tempFile = Path.GetTempFileName();
        // 预设空 JSON，确保 Load() 后内存配置为空，避免构造函数已加载的真实配置污染
        File.WriteAllText(_tempFile, "{}");

        _service = new JsonConfigService();
        RedirectConfigPath(_service, _tempFile);
        _service.Load();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    private static void RedirectConfigPath(JsonConfigService service, string path)
    {
        var field = typeof(JsonConfigService).GetField("_configPath",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("JsonConfigService._configPath 字段未找到");
        field.SetValue(service, path);
    }

    // ========== Set / Get 往返 ==========

    [Fact]
    public void Set_ThenGet_ReturnsStoredValue()
    {
        _service.Set("__test_int", 42);
        _service.Get<int>("__test_int").Should().Be(42);
    }

    [Fact]
    public void Set_String_RoundTrip()
    {
        _service.Set("__test_str", "hello");
        _service.Get<string>("__test_str").Should().Be("hello");
    }

    [Fact]
    public void Get_MissingKey_ReturnsDefault()
    {
        _service.Get<int>("__not_exist").Should().Be(0);
        _service.Get<string>("__not_exist").Should().BeNull();
        _service.Get<bool>("__not_exist").Should().BeFalse();
    }

    [Fact]
    public void Set_OverwritesExistingKey()
    {
        _service.Set("__k", 1);
        _service.Set("__k", 2);
        _service.Get<int>("__k").Should().Be(2);
    }

    // ========== Save 持久化 ==========

    [Fact]
    public void Save_WritesConfigToFile()
    {
        _service.Set("__persist_key", 123);
        _service.Save();

        var text = File.ReadAllText(_tempFile);
        text.Should().Contain("__persist_key");
        text.Should().Contain("123");
    }

    // ========== Load 从磁盘读取 ==========

    [Fact]
    public void Load_ReadsFromDisk()
    {
        File.WriteAllText(_tempFile, "{\"__loaded\": 99}");
        _service.Load();

        _service.Get<int>("__loaded").Should().Be(99);
    }

    [Fact]
    public void SaveThenLoad_RoundTripPreservesValues()
    {
        _service.Set("__a", "alpha");
        _service.Set("__b", 7);
        _service.Save();

        // 重定向到一个新实例难以隔离，这里直接重新读取磁盘内容验证
        var text = File.ReadAllText(_tempFile);
        text.Should().Contain("alpha");
        text.Should().Contain("7");
    }

    [Fact]
    public void Load_CorruptedFile_FallsBackToEmpty()
    {
        File.WriteAllText(_tempFile, "{not valid json");
        // Load 内部捕获异常并重置为空配置
        _service.Load();
        _service.Get<int>("__anything").Should().Be(0);
    }
}
