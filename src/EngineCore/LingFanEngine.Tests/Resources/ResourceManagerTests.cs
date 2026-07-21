using System.Security.Cryptography;
using FluentAssertions;
using LingFanEngine.Services.Resources;
using Xunit;

namespace LingFanEngine.Tests.Resources;

/// <summary>
/// ResourceManager 资源管理器测试
/// <para>覆盖 LoadBytesAsync（文件 + 缓存 + 加密包）、Exists、Evict、ClearCache、GetStats、GetFullPath。</para>
/// </summary>
public class ResourceManagerTests : IDisposable
{
    private readonly string _baseDir;
    private readonly ResourceManager _manager;

    public ResourceManagerTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), $"lf_rm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDir);
        _manager = new ResourceManager(_baseDir);
    }

    public void Dispose()
    {
        _manager.Dispose();
        if (Directory.Exists(_baseDir))
            Directory.Delete(_baseDir, true);
    }

    // ========== LoadBytesAsync 文件 ==========

    [Fact]
    public async Task LoadBytesAsync_ReadsFileContent()
    {
        var content = new byte[] { 10, 20, 30, 40 };
        await File.WriteAllBytesAsync(Path.Combine(_baseDir, "data.bin"), content);

        var result = await _manager.LoadBytesAsync("data.bin");

        result.Should().NotBeNull();
        result!.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task LoadBytesAsync_MissingFile_ReturnsNull()
    {
        var result = await _manager.LoadBytesAsync("nope.bin");
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadBytesAsync_CachesResult_SecondCallReturnsSameReference()
    {
        var content = new byte[] { 1, 2, 3 };
        await File.WriteAllBytesAsync(Path.Combine(_baseDir, "cache.bin"), content);

        var first = await _manager.LoadBytesAsync("cache.bin");
        var second = await _manager.LoadBytesAsync("cache.bin");

        first.Should().NotBeNull();
        first.Should().BeSameAs(second);
        _manager.GetStats().EntryCount.Should().Be(1);
    }

    [Fact]
    public async Task LoadBytesAsync_UpdatesCacheBytes()
    {
        var content = new byte[2048];
        RandomNumberGenerator.Fill(content);
        await File.WriteAllBytesAsync(Path.Combine(_baseDir, "big.bin"), content);

        await _manager.LoadBytesAsync("big.bin");

        _manager.GetStats().CurrentBytes.Should().Be(2048);
    }

    // ========== GetFullPath ==========

    [Fact]
    public void GetFullPath_Relative_CombinesWithBase()
    {
        var full = _manager.GetFullPath("a/b.png");
        full.Should().Be(Path.Combine(_baseDir, "a/b.png"));
    }

    [Fact]
    public void GetFullPath_Rooted_ReturnsAsIs()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "x.png");
        _manager.GetFullPath(rooted).Should().Be(rooted);
    }

    // ========== Exists ==========

    [Fact]
    public async Task Exists_AfterLoad_ReturnsTrue()
    {
        await File.WriteAllBytesAsync(Path.Combine(_baseDir, "e.bin"), new byte[] { 1 });
        await _manager.LoadBytesAsync("e.bin");
        _manager.Exists("e.bin").Should().BeTrue();
    }

    [Fact]
    public void Exists_Nonexistent_ReturnsFalse()
    {
        _manager.Exists("ghost.bin").Should().BeFalse();
    }

    // ========== Evict / ClearCache ==========

    [Fact]
    public async Task Evict_RemovesFromCache()
    {
        await File.WriteAllBytesAsync(Path.Combine(_baseDir, "v.bin"), new byte[] { 9 });
        await _manager.LoadBytesAsync("v.bin");

        _manager.Evict("v.bin").Should().BeTrue();
        _manager.GetStats().EntryCount.Should().Be(0);
        // 注：Evict 仅清除内存缓存，不删除磁盘文件；
        // Exists 在文件仍存在时返回 true 属预期行为（资源仍可被重新加载）。
    }

    [Fact]
    public async Task ClearCache_EmptiesAll()
    {
        await File.WriteAllBytesAsync(Path.Combine(_baseDir, "c1.bin"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(_baseDir, "c2.bin"), new byte[] { 2 });
        await _manager.LoadBytesAsync("c1.bin");
        await _manager.LoadBytesAsync("c2.bin");

        _manager.ClearCache();
        _manager.GetStats().EntryCount.Should().Be(0);
        _manager.GetStats().CurrentBytes.Should().Be(0);
    }

    // ========== 加密包加载 ==========

    [Fact]
    public async Task LoadBytesAsync_FromMountedPack_ReturnsPackData()
    {
        // 构建临时源目录与 .lfpack 包
        var srcDir = Path.Combine(_baseDir, "src");
        var innerDir = Path.Combine(srcDir, "inner");
        Directory.CreateDirectory(innerDir);
        var packContent = new byte[] { 100, 101, 102, 103 };
        await File.WriteAllBytesAsync(Path.Combine(innerDir, "note.txt"), packContent);

        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        var packPath = Path.Combine(_baseDir, "res.lfpack");
        await PackBuilder.BuildAsync(srcDir, packPath, key);

        await _manager.MountPackAsync(packPath, key);

        var loaded = await _manager.LoadBytesAsync("inner/note.txt");
        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(packContent);

        // 包内文件也应被 Exists 识别
        _manager.Exists("inner/note.txt").Should().BeTrue();
    }
}
