using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// 空音频播放器——引擎默认实现，不输出任何声音。
/// <para>开发者可实现 IAudioPlayer 并注入到 AudioManager 构造函数以接入真实音频后端。</para>
/// <para>Browser/WASM 等不支持 LibVLC 的平台自动降级为此实现。</para>
/// </summary>
public sealed class NullAsyncAudioPlayer : IAudioPlayer
{
    private PlaybackState _state = PlaybackState.Stopped;

    public Task LoadAsync(string source, CancellationToken ct = default) => Task.CompletedTask;

    public Task PlayAsync(CancellationToken ct = default)
    {
        _state = PlaybackState.Playing;
        if (ct.CanBeCanceled)
        {
            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }
        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken ct = default)
    {
        _state = PlaybackState.Paused;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _state = PlaybackState.Stopped;
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position, CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask<TimeSpan> GetPositionAsync() => ValueTask.FromResult(TimeSpan.Zero);
    public ValueTask<TimeSpan> GetDurationAsync() => ValueTask.FromResult(TimeSpan.Zero);
    public ValueTask SetVolumeAsync(float volume) => ValueTask.CompletedTask;
    public ValueTask SetMuteAsync(bool mute) => ValueTask.CompletedTask;
    public ValueTask<PlaybackState> GetStateAsync() => ValueTask.FromResult(_state);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
