using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Story;

/// <summary>
/// StoryLoader 加载管线测试
/// <para>覆盖 JSON / 纯 DSL 两种 .story 格式的 LoadFromFileAsync、commands/labels 解析、LoadedCount 与 Clear。</para>
/// </summary>
public class StoryLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public StoryLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lf_story_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private static StoryLoader CreateLoader(out FakeSceneRegistry sceneReg, out FakeCommandPipeline pipeline, out StateContainer state)
    {
        var engine = new LingFanDslEngine();
        state = new StateContainer();
        sceneReg = new FakeSceneRegistry();
        pipeline = new FakeCommandPipeline();
        return new StoryLoader(engine, pipeline, state, sceneReg);
    }

    [Fact]
    public async Task LoadFromFileAsync_JsonStory_ParsesIdLangCommandsAndLabels()
    {
        var loader = CreateLoader(out _, out _, out _);
        var file = Path.Combine(_tempDir, "demo_zh-CN.story");
        await File.WriteAllTextAsync(file, """
            {
              "id": "demo",
              "lang": "zh-CN",
              "script": "label intro:\n  say \"欢迎\""
            }
            """);

        var story = await loader.LoadFromFileAsync(file);

        story.Should().NotBeNull();
        story!.Id.Should().Be("demo");
        story.Lang.Should().Be("zh-CN");
        story.CompiledCommands.Should().NotBeNull();
        story.CompiledCommands.Should().NotBeEmpty();
        story.Labels.Should().NotBeNull();
        story.Labels!.Should().ContainKey("intro");
    }

    [Fact]
    public async Task LoadFromFileAsync_PureDsl_ParsesFromFileNameAndCompiles()
    {
        var loader = CreateLoader(out _, out _, out _);
        var file = Path.Combine(_tempDir, "plain_zh-CN.story");
        await File.WriteAllTextAsync(file, "label start:\n  say \"hi\"");

        var story = await loader.LoadFromFileAsync(file);

        story.Should().NotBeNull();
        story!.Id.Should().Be("plain");
        story.Lang.Should().Be("zh-CN");
        story.CompiledCommands.Should().NotBeNull().And.NotBeEmpty();
        story.Labels.Should().NotBeNull();
        story.Labels!.Should().ContainKey("start");
    }

    [Fact]
    public async Task LoadFromFileAsync_MissingFile_ReturnsNull()
    {
        var loader = CreateLoader(out _, out _, out _);
        var story = await loader.LoadFromFileAsync(Path.Combine(_tempDir, "ghost_zh-CN.story"));
        story.Should().BeNull();
    }

    [Fact]
    public async Task LoadFromFileAsync_IncrementsLoadedCount()
    {
        var loader = CreateLoader(out _, out _, out _);
        var file = Path.Combine(_tempDir, "demo_zh-CN.story");
        await File.WriteAllTextAsync(file, """
            {
              "id": "demo",
              "lang": "zh-CN",
              "script": "label intro:\n  say \"欢迎\""
            }
            """);

        await loader.LoadFromFileAsync(file);

        loader.LoadedCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Clear_RemovesAllLoadedStories()
    {
        var loader = CreateLoader(out _, out _, out _);
        var file = Path.Combine(_tempDir, "demo_zh-CN.story");
        await File.WriteAllTextAsync(file, """
            {
              "id": "demo",
              "lang": "zh-CN",
              "script": "label intro:\n  say \"欢迎\""
            }
            """);
        await loader.LoadFromFileAsync(file);
        loader.LoadedCount.Should().BeGreaterThanOrEqualTo(1);

        loader.Clear();

        loader.LoadedCount.Should().Be(0);
    }
}
