using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using FluentAssertions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Serialization;
using LingFanEngine.Services.Resources;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// PackBuilder 测试：使用临时目录构建 .lfpack，验证文件头魔数、manifest 文件列表与加解密 round-trip。
/// <para>PackBuilder 仅使用 System.IO.Compression / System.Security.Cryptography，不涉及 Avalonia/真实资源。</para>
/// </summary>
public class PackBuilderTests
{
    private static byte[] MakeKey() => new byte[32]; // AES-256 需要 32 字节

    [Fact]
    public async Task BuildAsync_WritesLfpackWithCorrectMagic()
    {
        var source = Path.Combine(Path.GetTempPath(), "lfpack_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "lfpack_out_" + Guid.NewGuid().ToString("N") + ".lfpack");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "hello");
        try
        {
            await PackBuilder.BuildAsync(source, output, MakeKey());

            File.Exists(output).Should().BeTrue();
            var bytes = await File.ReadAllBytesAsync(output);
            // 前 4 字节应为 LFPK 魔数
            bytes[0].Should().Be((byte)'L');
            bytes[1].Should().Be((byte)'F');
            bytes[2].Should().Be((byte)'P');
            bytes[3].Should().Be((byte)'K');
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
            Directory.Delete(source, true);
        }
    }

    [Fact]
    public async Task BuildAsync_ManifestContainsAllFiles()
    {
        var source = Path.Combine(Path.GetTempPath(), "lfpack_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "lfpack_out_" + Guid.NewGuid().ToString("N") + ".lfpack");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(source, "b.txt"), "2");
        try
        {
            await PackBuilder.BuildAsync(source, output, MakeKey());

            var manifest = ReadManifest(output);
            manifest.Files.Should().BeEquivalentTo(new[] { "a.txt", "b.txt" });
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
            Directory.Delete(source, true);
        }
    }

    [Fact]
    public async Task BuildAsync_PreservesFileContentsAfterDecrypt()
    {
        var source = Path.Combine(Path.GetTempPath(), "lfpack_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "lfpack_out_" + Guid.NewGuid().ToString("N") + ".lfpack");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "data.txt"), "payload-123");
        try
        {
            await PackBuilder.BuildAsync(source, output, MakeKey());

            // 用 PackLoader 的挂载能力做 round-trip 验证（不依赖真实资源）
            var loader = new PackLoader();
            await loader.MountAsync(output, MakeKey());
            var content = await loader.ReadBytesAsync("data.txt");
            content.Should().NotBeNull();
            Encoding.UTF8.GetString(content!).Should().Be("payload-123");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
            Directory.Delete(source, true);
        }
    }

    private static PackManifest ReadManifest(string outputPath)
    {
        var bytes = File.ReadAllBytes(outputPath);
        var manifestLen = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
        var manifestJson = Encoding.UTF8.GetString(bytes, 8, manifestLen);
        return JsonSerializer.Deserialize(manifestJson, LfJsonContext.Default.PackManifest)!;
    }
}
