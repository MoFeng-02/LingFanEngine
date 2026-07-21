using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Story;

/// <summary>
/// StoryRegistry 懒加载注册表测试
/// <para>覆盖 Scan 注册、LoadSceneFromFile 编译缓存、ReloadFile 热重载、CanLoad 查找。</para>
/// </summary>
public class StoryRegistryTests : IDisposable
{
    private readonly string _tempDir;

    public StoryRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lf_reg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private StoryRegistry CreateRegistry(out FakeSceneRegistry sceneReg, string? storyRoot = null)
    {
        var engine = new LingFanDslEngine();
        var state = new StateContainer();
        sceneReg = new FakeSceneRegistry();
        var pipeline = new FakeCommandPipeline();
        var loader = new StoryLoader(engine, pipeline, state, sceneReg);
        return new StoryRegistry(sceneReg, engine, loader, storyRoot: storyRoot ?? _tempDir);
    }

    [Fact]
    public void Scan_RegistersScenesFromDirectory()
    {
        File.WriteAllText(Path.Combine(_tempDir, "chap.story"), "scene \"intro\"\nsay \"hi\"");
        var registry = CreateRegistry(out _);

        registry.Scan();

        registry.RegisteredCount.Should().Be(1);
    }

    [Fact]
    public void LoadSceneFromFile_CompilesAndCachesResult()
    {
        var file = Path.Combine(_tempDir, "chap.story");
        File.WriteAllText(file, "scene \"intro\"\nsay \"hi\"");
        var registry = CreateRegistry(out _);

        var ok = registry.LoadSceneFromFile(file);

        ok.Should().BeTrue();
        var (commands, labels) = registry.GetCompiledResultByFile(file);
        commands.Should().NotBeNull();
        commands!.Should().NotBeEmpty();
    }

    [Fact]
    public void LoadSceneFromFile_MissingFile_ReturnsFalse()
    {
        var registry = CreateRegistry(out _);

        var ok = registry.LoadSceneFromFile(Path.Combine(_tempDir, "ghost.story"));

        ok.Should().BeFalse();
    }

    [Fact]
    public void ReloadFile_RecompilesAndReturnsAffectedScenes()
    {
        var file = Path.Combine(_tempDir, "chap.story");
        File.WriteAllText(file, "scene \"intro\"\nsay \"hi\"");
        var registry = CreateRegistry(out _);
        registry.Scan();
        registry.LoadSceneFromFile(file);

        var affected = registry.ReloadFile(file);

        affected.Should().Contain("intro");
        var (commands, _) = registry.GetCompiledResultByFile(file);
        commands.Should().NotBeNull();
    }

    [Fact]
    public void CanLoad_UnknownScene_ReturnsFalse()
    {
        var registry = CreateRegistry(out _);

        registry.CanLoad("nonexistent_scene_xyz").Should().BeFalse();
    }
}
