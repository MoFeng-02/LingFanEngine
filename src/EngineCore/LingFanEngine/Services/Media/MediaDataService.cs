using System.Diagnostics;
using System.Text.Json;
using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// 媒体数据服务实现
/// <para>只负责返回媒体资源的数据和信息，不负责实际播放</para>
/// </summary>
public class MediaDataService : IMediaDataService
{
    private readonly string _mediaBasePath;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="mediaBasePath">媒体资源根目录</param>
    public MediaDataService(string mediaBasePath = "Media")
    {
        _mediaBasePath = mediaBasePath;
    }

    /// <inheritdoc/>
    public async Task<IVideoData?> GetVideoDataAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
            return null;

        // 尝试使用 FFprobe 获取视频元数据
        var metadata = await TryGetVideoMetadataAsync(fullPath, ct);

        return new VideoData
        {
            Path = path,
            Loop = false,
            Volume = 1.0f,
            Width = metadata.Width,
            Height = metadata.Height,
            Duration = metadata.Duration
        };
    }

    /// <inheritdoc/>
    public async Task<IAudioData> GetAudioDataAsync(string path, string? channel = null, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);

        // 尝试使用 FFprobe 获取音频元数据
        var metadata = await TryGetAudioMetadataAsync(fullPath, ct);

        return new AudioData
        {
            Path = path,
            Channel = channel ?? "default",
            Loop = false,
            Volume = 1.0f,
            Duration = metadata
        };
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string path)
    {
        var fullPath = GetFullPath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    /// <summary>
    /// 获取完整路径
    /// </summary>
    private string GetFullPath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.Combine(_mediaBasePath, path);
    }

    /// <summary>
    /// 尝试使用 FFprobe 获取视频元数据
    /// </summary>
    private async Task<(int Width, int Height, double Duration)> TryGetVideoMetadataAsync(string path, CancellationToken ct)
    {
        try
        {
            var ffprobePath = FindFfprobePath();
            if (ffprobePath == null)
                return (0, 0, 0);

            var output = await RunFfprobeAsync(ffprobePath, path, ct);
            return ParseFfprobeOutput(output);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    /// <summary>
    /// 尝试使用 FFprobe 获取音频元数据
    /// </summary>
    private async Task<double> TryGetAudioMetadataAsync(string path, CancellationToken ct)
    {
        try
        {
            var ffprobePath = FindFfprobePath();
            if (ffprobePath == null)
                return 0;

            var output = await RunFfprobeAsync(ffprobePath, path, ct);
            return ParseAudioDuration(output);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 查找 FFprobe 路径
    /// </summary>
    private static string? FindFfprobePath()
    {
        // 检查常见路径
        var possiblePaths = new[]
        {
            "ffprobe",
            "ffprobe.exe",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffprobe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffprobe.exe"),
            @"C:\ffmpeg\bin\ffprobe.exe",
            @"/usr/bin/ffprobe"
        };

        foreach (var p in possiblePaths)
        {
            if (File.Exists(p))
                return p;
        }

        // 尝试从 PATH 中查找
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = "ffprobe",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    var lines = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0)
                        return lines[0].Trim();
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 运行 FFprobe 获取元数据
    /// </summary>
    private static async Task<string> RunFfprobeAsync(string ffprobePath, string filePath, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start ffprobe");

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return output;
    }

    /// <summary>
    /// 解析 FFprobe 输出
    /// </summary>
    private static (int Width, int Height, double Duration) ParseFfprobeOutput(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 获取时长
            double duration = 0;
            if (root.TryGetProperty("format", out var format) &&
                format.TryGetProperty("duration", out var durationElement))
            {
                duration = double.Parse(durationElement.GetString() ?? "0");
            }

            // 获取视频流信息
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    if (stream.TryGetProperty("codec_type", out var codecType) &&
                        codecType.GetString() == "video")
                    {
                        var width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                        var height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                        return (width, height, duration);
                    }
                }
            }

            return (0, 0, duration);
        }
        catch
        {
            return (0, 0, 0);
        }
    }

    /// <summary>
    /// 解析音频时长
    /// </summary>
    private static double ParseAudioDuration(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("format", out var format) &&
                format.TryGetProperty("duration", out var durationElement))
            {
                return double.Parse(durationElement.GetString() ?? "0");
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }
}