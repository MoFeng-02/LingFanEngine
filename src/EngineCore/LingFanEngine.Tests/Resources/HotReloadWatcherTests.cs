using System.Linq;
using System.Reflection;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Resources;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Resources;

/// <summary>
/// HotReloadWatcher 资源热重载监视器测试
/// <para>覆盖 Watch 创建目录、文件变更事件回调、状态脏标记与命令投递（通过反射调用私有回调）。</para>
/// </summary>
public class HotReloadWatcherTests : IDisposable
{
    private readonly string _dir;

    public HotReloadWatcherTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lf_hrw_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public void Watch_NonExistentDirectory_CreatesItAndReturns()
    {
        var watcher = new HotReloadWatcher(new FakeCommandPipeline(), new StateContainer());
        var missing = Path.Combine(_dir, "sub");

        watcher.Watch(missing);

        Directory.Exists(missing).Should().BeTrue();
    }

    [Fact]
    public void OnChanged_RaisesEventAndMarksDirtyAndSendsCommand()
    {
        var state = new StateContainer();
        var pipeline = new FakeCommandPipeline();
        var watcher = new HotReloadWatcher(pipeline, state);
        HotReloadEventArgs? captured = null;
        watcher.OnFileChanged += a => captured = a;

        var file = Path.Combine(_dir, "image.png");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(file, "x");
        var method = typeof(HotReloadWatcher)
            .GetMethod("OnChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(watcher, new object?[] { null, new FileSystemEventArgs(WatcherChangeTypes.Changed, _dir, "image.png") });

        captured.Should().NotBeNull();
        captured!.FilePath.Should().EndWith("image.png");
        captured.ChangeType.Should().Be(WatcherChangeTypes.Changed);
        state.Get<bool>(StateKeys.Scene.Dirty).Should().BeTrue();
        pipeline.Sent.OfType<SetVariableCommand>().Should().Contain(sv => sv.Key.Contains("hotreload_"));
    }

    [Fact]
    public void OnRenamed_RaisesEventAndMarksDirty()
    {
        var state = new StateContainer();
        var pipeline = new FakeCommandPipeline();
        var watcher = new HotReloadWatcher(pipeline, state);
        HotReloadEventArgs? captured = null;
        watcher.OnFileChanged += a => captured = a;

        var method = typeof(HotReloadWatcher)
            .GetMethod("OnRenamed", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(watcher, new object?[]
        {
            null,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, _dir, "new.png", "old.png")
        });

        captured.Should().NotBeNull();
        captured!.OldFilePath.Should().EndWith("old.png");
        state.Get<bool>(StateKeys.Scene.Dirty).Should().BeTrue();
    }

    [Fact]
    public void Stop_DoesNotThrow()
    {
        var watcher = new HotReloadWatcher(new FakeCommandPipeline(), new StateContainer());

        var act = () => watcher.Stop();

        act.Should().NotThrow();
    }
}
