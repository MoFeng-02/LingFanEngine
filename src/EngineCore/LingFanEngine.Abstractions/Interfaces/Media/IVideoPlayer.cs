namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 视频播放器抽象接口（控件模型包装）。
/// <para>引擎不在此提供实现；Desktop 由 LingFanEngine.Views 内的 GpuMediaPlayerVideoPlayer 包装
/// MediaPlayer.Controls.GpuMediaPlayer 实现（库按 OS 自动选后端：Windows→MediaFoundation /
/// macOS→AVFoundation / Linux→FFmpeg 或 LibVLC）。</para>
/// <para>Browser/WASM、Android、iOS 平台无 GpuMediaPlayer 后端，引擎默认注入 NullVideoPlayer
/// （Control 返回 null，VideoPresenter 优雅跳过）。宿主可注册自定义 IVideoPlayer 实现恢复视频功能：</para>
/// <list type="bullet">
/// <item>Browser/WASM：使用 HTML &lt;video&gt; 直出</item>
/// <item>Android/iOS：使用 LibVLCSharp 自定义实现</item>
/// </list>
/// <para>音视频分离架构：视频播放器永久静音（Volume=0），音频走独立的 AudioManager。</para>
/// </summary>
public interface IVideoPlayer
{
    /// <summary>
    /// 承载视频渲染的视觉控件句柄（Desktop = GpuMediaPlayer；非 Avalonia / 测试环境返回 null）。
    /// 使用者（VideoPresenter）应做 <c>if (Control is Control c) ...</c> 守卫，null 时跳过可视树挂载。
    /// </summary>
    object? Control { get; }
}
