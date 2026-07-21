using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Saves;
using Xunit;

namespace LingFanEngine.Tests.Saves;

/// <summary>
/// 跨机存档迁移测试（盲区验证 T12）。
/// <para>生产密钥由 BinarySaveService.GenerateDefaultEncryption 用 $"{MachineName}_{UserName}" → SHA256 派生；
/// 换机/换用户 = 不同密钥 = 解密失败。本测试用注入指定 key 的构造函数确定性模拟「同机 / 跨机 / 旧格式 / 损坏」。</para>
/// </summary>
public class SaveCrossMachineTests
{
    private static (byte[] key, byte[] iv) MakeKey(string seed)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var iv = new byte[16];
        Array.Copy(key, iv, 16);
        return (key, iv);
    }

    [Fact]
    public async Task Save_WithKeyA_Load_WithSameKeyA_Succeeds()
    {
        using var dir = new TempDir();
        var (key, iv) = MakeKey("machineA_userX");
        var svc = new BinarySaveService(dir.Path, key, iv);

        await svc.SaveAsync("slot1", new SaveData { Name = "slot1", SceneName = "main", GameVersion = "1.0.0" });
        var loaded = await svc.LoadAsync("slot1");

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("slot1");
    }

    [Fact]
    public async Task Save_WithKeyA_Load_WithKeyB_Fails_ReturnsNull()
    {
        // 模拟跨机（不同 MachineName/UserName → 不同派生密钥）
        using var dir = new TempDir();
        var (keyA, ivA) = MakeKey("machineA_userX");
        var (keyB, ivB) = MakeKey("machineB_userY");

        var svcA = new BinarySaveService(dir.Path, keyA, ivA);
        await svcA.SaveAsync("secret", new SaveData { Name = "secret", SceneName = "main", GameVersion = "1.0.0" });

        var svcB = new BinarySaveService(dir.Path, keyB, ivB);
        var loaded = await svcB.LoadAsync("secret"); // 解密失败 → null

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task OldFormat_OnlySav_NoMeta_LoadsSuccessfully()
    {
        // 模拟 Phase 36 之前只有 .sav（无 .meta 索引）的旧存档：LoadAsync 直接解密完整存档
        using var dir = new TempDir();
        var (key, iv) = MakeKey("machineA_userX");
        var svc = new BinarySaveService(dir.Path, key, iv);

        await svc.SaveAsync("legacy", new SaveData { Name = "legacy", SceneName = "main", GameVersion = "1.0.0" });
        var metaFile = System.IO.Directory.GetFiles(dir.Path, "legacy.meta")[0];
        System.IO.File.Delete(metaFile); // 删掉新格式索引，回到旧格式

        var loaded = await svc.LoadAsync("legacy");
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("legacy");
    }

    [Fact]
    public async Task CorruptedFile_Load_ReturnsNull()
    {
        using var dir = new TempDir();
        var (key, iv) = MakeKey("machineA_userX");
        var svc = new BinarySaveService(dir.Path, key, iv);

        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir.Path, "broken.sav"), new byte[] { 1, 2, 3, 4 });
        var loaded = await svc.LoadAsync("broken");

        loaded.Should().BeNull();
    }

    private sealed class TempDir : System.IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lf_save_" + System.Guid.NewGuid().ToString("N"));

        public TempDir() => System.IO.Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, true); } catch { }
        }
    }
}
