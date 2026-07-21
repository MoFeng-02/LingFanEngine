using System.Security.Cryptography;
using FluentAssertions;
using LingFanEngine.Services.Resources;
using Xunit;

namespace LingFanEngine.Tests.Resources;

/// <summary>
/// PackLoader 加密资源包加载器测试
/// <para>使用 PackBuilder 构建 .lfpack（LFPK/AES-GCM 新格式），验证 Mount/Read/Exists/Unmount/Manifest，
/// 以及旧格式（CBC）缺 IV、数据过短等异常路径。</para>
/// </summary>
public class PackLoaderTests : IDisposable
{
    private readonly string _workDir;
    private readonly byte[] _key;

    public PackLoaderTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"lf_pack_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        _key = new byte[32];
        RandomNumberGenerator.Fill(_key);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, true);
    }

    private async Task<string> BuildPack(Dictionary<string, byte[]> files)
    {
        var srcDir = Path.Combine(_workDir, "src_" + Guid.NewGuid().ToString("N"));
        foreach (var (rel, data) in files)
        {
            var full = Path.Combine(srcDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, data);
        }

        var packPath = Path.Combine(_workDir, "pack_" + Guid.NewGuid().ToString("N") + ".lfpack");
        await PackBuilder.BuildAsync(srcDir, packPath, _key);
        return packPath;
    }

    // ========== Mount 新格式 ==========

    [Fact]
    public async Task MountAsync_LoadsAllFiles()
    {
        var packPath = await BuildPack(new()
        {
            ["a.txt"] = new byte[] { 1, 2, 3 },
            ["sub/b.txt"] = new byte[] { 4, 5, 6, 7 }
        });

        var loader = new PackLoader();
        await loader.MountAsync(packPath, _key);

        loader.FileCount.Should().Be(2);
        loader.Exists("a.txt").Should().BeTrue();
        loader.Exists("sub/b.txt").Should().BeTrue();
    }

    [Fact]
    public async Task ReadBytesAsync_ReturnsPackContent()
    {
        var packPath = await BuildPack(new()
        {
            ["doc.txt"] = new byte[] { 11, 22, 33 }
        });

        var loader = new PackLoader();
        await loader.MountAsync(packPath, _key);

        var data = await loader.ReadBytesAsync("doc.txt");
        data.Should().NotBeNull();
        data!.Should().BeEquivalentTo(new byte[] { 11, 22, 33 });
    }

    [Fact]
    public async Task ReadBytesAsync_MissingFile_ReturnsNull()
    {
        var packPath = await BuildPack(new() { ["x.txt"] = new byte[] { 1 } });
        var loader = new PackLoader();
        await loader.MountAsync(packPath, _key);

        (await loader.ReadBytesAsync("missing.txt")).Should().BeNull();
    }

    [Fact]
    public async Task GetManifest_ReturnsFileList()
    {
        var packPath = await BuildPack(new()
        {
            ["f1"] = new byte[] { 1 },
            ["f2"] = new byte[] { 2 }
        });

        var loader = new PackLoader();
        await loader.MountAsync(packPath, _key);

        var packId = Path.GetFileNameWithoutExtension(packPath);
        var manifest = loader.GetManifest(packId);
        manifest.Should().NotBeNull();
        manifest!.Files.Should().Contain(new[] { "f1", "f2" });
        loader.MountedPacks.Should().Contain(packId);
    }

    [Fact]
    public async Task Unmount_RemovesPackFiles()
    {
        var packPath = await BuildPack(new() { ["u.txt"] = new byte[] { 9 } });
        var loader = new PackLoader();
        await loader.MountAsync(packPath, _key);
        var packId = Path.GetFileNameWithoutExtension(packPath);

        loader.Unmount(packId);

        loader.FileCount.Should().Be(0);
        loader.Exists("u.txt").Should().BeFalse();
        loader.GetManifest(packId).Should().BeNull();
    }

    // ========== 异常路径 ==========

    [Fact]
    public async Task MountAsync_LegacyFormatWithoutIv_Throws()
    {
        // 非 LFPK 魔数的数据（旧 CBC 格式），且不提供 iv → 应抛 ArgumentException
        var badPath = Path.Combine(_workDir, "bad.bin");
        await File.WriteAllBytesAsync(badPath, new byte[] { 1, 2, 3, 4, 5 });

        var loader = new PackLoader();
        await Assert.ThrowsAsync<ArgumentException>(() => loader.MountAsync(badPath, _key).AsTask());
    }

    [Fact]
    public async Task MountAsync_WrongKeyLength_Throws()
    {
        var packPath = await BuildPack(new() { ["k.txt"] = new byte[] { 1 } });
        var shortKey = new byte[16];
        var loader = new PackLoader();

        await Assert.ThrowsAsync<ArgumentException>(() => loader.MountAsync(packPath, shortKey).AsTask());
    }
}
