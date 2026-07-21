using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Story;

/// <summary>
/// StoryHotReloadService 热重载服务测试
/// <para>覆盖启用/禁用开关、目录缺失防御、以及文件变更触发的重载与通知行为（通过反射调用私有回调）。</para>
/// </summary>
public class StoryHotReloadServiceTests : IDisposable
{
    private readonly string _tempDir;

    public StoryHotReloadServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lf_hrs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Start_WhenHotReloadDisabled_DoesNotThrow()
    {
        var state = new StateContainer();
        var pipeline = new FakeCommandPipeline();
        var reg = new FakeStoryRegistry();
        var options = new LingFanEngineOptions { EnableHotReload = false };
        var svc = new StoryHotReloadService(reg, state, pipeline, options);

        var act = () => svc.Start();

        act.Should().NotThrow();
    }

    [Fact]
    public void Start_WhenHotReloadEnabledButDirMissing_DoesNotThrow()
    {
        var state = new StateContainer();
        var pipeline = new FakeCommandPipeline();
        var reg = new FakeStoryRegistry();
        var options = new LingFanEngineOptions
        {
            EnableHotReload = true,
            StoriesDirectory = Path.Combine(Path.GetTempPath(), "lf_missing_" + Guid.NewGuid().ToString("N"))
        };
        var svc = new StoryHotReloadService(reg, state, pipeline, options);

        var act = () => svc.Start();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task OnStoryFileChanged_TriggersReloadAndNotifiesCurrentScene()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentName, "intro");
        var pipeline = new FakeCommandPipeline();
        var reg = new FakeStoryRegistry { ReloadResult = new List<string> { "intro" } };
        var options = new LingFanEngineOptions { EnableHotReload = true, StoriesDirectory = _tempDir };
        var svc = new StoryHotReloadService(reg, state, pipeline, options);

        var file = Path.Combine(_tempDir, "demo.story");
        var method = typeof(StoryHotReloadService)
            .GetMethod("OnStoryFileChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(svc, new object?[] { null, new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "demo.story") });

        // 重载在 200ms 防抖延迟后同步执行；轮询等待 Notify.Text 真正写入再断言
        for (int i = 0; i < 100 && state.Get<string>(StateKeys.Notify.Text) == null; i++)
            await Task.Delay(50);

        reg.ReloadedFiles.Should().Contain(file);
        state.Get<string>(StateKeys.Notify.Text).Should().NotBeNull();
        pipeline.Sent.Should().Contain(c => c is NavigateCommand);
    }

    [Fact]
    public async Task OnStoryFileChanged_AffectedSceneNotCurrent_OnlyNotifies()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentName, "other");
        var pipeline = new FakeCommandPipeline();
        var reg = new FakeStoryRegistry { ReloadResult = new List<string> { "intro" } };
        var options = new LingFanEngineOptions { EnableHotReload = true, StoriesDirectory = _tempDir };
        var svc = new StoryHotReloadService(reg, state, pipeline, options);

        var file = Path.Combine(_tempDir, "demo.story");
        var method = typeof(StoryHotReloadService)
            .GetMethod("OnStoryFileChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(svc, new object?[] { null, new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "demo.story") });

        // 重载在 200ms 防抖延迟后同步执行；轮询等待 Notify.Text 真正写入再断言
        for (int i = 0; i < 100 && state.Get<string>(StateKeys.Notify.Text) == null; i++)
            await Task.Delay(50);

        reg.ReloadedFiles.Should().Contain(file);
        state.Get<string>(StateKeys.Notify.Text).Should().NotBeNull();
        pipeline.Sent.Should().NotContain(c => c is NavigateCommand);
    }
}
