using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Services.Media;
using Xunit;

namespace LingFanEngine.Tests.Media;

/// <summary>
/// MediaDataService 媒体数据服务测试
/// <para>覆盖 ExistsAsync 存在/缺失判断、GetAudioDataAsync 默认数据、GetVideoDataAsync 缺失返回 null。</para>
/// <para>仅测数据查询逻辑，不依赖 ffprobe（缺失时元数据默认为 0）。</para>
/// </summary>
public class MediaDataServiceTests
{
    [Fact]
    public async Task ExistsAsync_ExistingRootedFile_ReturnsTrue()
    {
        var temp = Path.GetTempFileName();
        try
        {
            var svc = new MediaDataService();
            (await svc.ExistsAsync(temp)).Should().BeTrue();
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task ExistsAsync_MissingFile_ReturnsFalse()
    {
        var svc = new MediaDataService();
        var missing = Path.Combine(Path.GetTempPath(), "lf_missing_" + Guid.NewGuid().ToString("N") + ".mp3");

        (await svc.ExistsAsync(missing)).Should().BeFalse();
    }

    [Fact]
    public async Task GetAudioDataAsync_ReturnsDataWithDefaults()
    {
        var temp = Path.GetTempFileName();
        try
        {
            var svc = new MediaDataService();

            var data = await svc.GetAudioDataAsync(temp);

            data.Path.Should().Be(temp);
            data.Channel.Should().Be("default");
            data.Loop.Should().BeFalse();
            data.Volume.Should().Be(1.0f);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public async Task GetVideoDataAsync_MissingFile_ReturnsNull()
    {
        var svc = new MediaDataService();
        var missing = Path.Combine(Path.GetTempPath(), "lf_ghost_" + Guid.NewGuid().ToString("N") + ".webm");

        var data = await svc.GetVideoDataAsync(missing);

        data.Should().BeNull();
    }
}
