using System;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Views;

/// <summary>
/// 无后端平台的视频播放器占位实现（Browser/WASM/Android/iOS 或视频不可用环境）。
/// <para>所有方法为空操作，状态恒为 <see cref="PlaybackState.Stopped"/>。
/// 视频由宿主外部处理（如 WASM 的 HTML &lt;video&gt; 直出）。</para>
/// </summary>
internal sealed class NullVideoPlayer : IVideoPlayer
{
    public Task LoadAsync(string uri, CancellationToken ct = default) => Task.CompletedTask;
    public Task PlayAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SeekAsync(TimeSpan position, CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask<TimeSpan> GetPositionAsync() => default;
    public ValueTask<TimeSpan> GetDurationAsync() => default;
    public ValueTask SetVolumeAsync(float volume) => default;
    public ValueTask SetMuteAsync(bool mute) => default;
    public ValueTask<PlaybackState> GetStateAsync() => new(PlaybackState.Stopped);
    public void Activate() { }
    public void Deactivate() { }
    public void SetZIndex(int z) { }
    public void SetBounds(double left, double top, double width, double height) { }

    public ValueTask DisposeAsync() => default;
}
