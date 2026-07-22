using LingFanEngine.Abstractions.Interfaces.Media;
using MediaPlayer.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// Desktop 视频播放器实现——包装 GpuMediaPlayer。
/// <para>MediaPlayer.Controls 按 OS 自动选择后端（Windows→MediaFoundation / macOS→AVFoundation /
/// Linux→FFmpeg 或 LibVLC），引擎代码不干预。</para>
/// <para>音画分离：视频控件默认静音（Volume=0），音频由独立的 AudioManager 管理。</para>
/// <para>容错：若原生后端初始化失败（如 Linux 未安装 libvlc/ffmpeg），Control 返回 null，
/// VideoPresenter 据此跳过可视树挂载，游戏继续运行（仅无视频画面）。</para>
/// </summary>
internal sealed class GpuMediaPlayerVideoPlayer : IVideoPlayer
{
    // 延迟创建：仅在首次需要（首个视频路径变更、UI 线程上的 Update）才实例化原生 GpuMediaPlayer，
    // 避免音频-only 游戏在窗口构造时就拉起视频原生引擎（原 hardcode 时期是 Update 内按需 new）。
    private GpuMediaPlayer? _gpu;
    private bool _initFailed;

    public object? Control
    {
        get
        {
            if (_initFailed) return null;
            if (_gpu == null)
            {
                try
                {
                    _gpu = new GpuMediaPlayer();
                }
                catch (Exception ex)
                {
                    _initFailed = true;
                    var hint = OperatingSystem.IsLinux()
                        ? "Linux 需安装 LibVLC 或 FFmpeg 原生库（如 apt install libvlc-dev 或 apt install ffmpeg）"
                        : OperatingSystem.IsMacOS()
                            ? "macOS 需 AVFoundation 框架（通常预装）"
                            : OperatingSystem.IsWindows()
                                ? "Windows 需 MediaFoundation 框架（通常预装）"
                                : "当前平台可能缺少视频后端原生库";
                    System.Diagnostics.Debug.WriteLine(
                        $"[Video] GpuMediaPlayer 初始化失败，视频功能将不可用。\n" +
                        $"  原因: {ex.GetType().Name}: {ex.Message}\n" +
                        $"  建议: {hint}");
                }
            }
            return _gpu;
        }
    }
}
