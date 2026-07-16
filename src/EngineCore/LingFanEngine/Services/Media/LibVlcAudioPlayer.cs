using LibVLCSharp.Shared;
using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// 基于 LibVLCSharp 的异步音频播放器——实现 IAudioPlayer
/// <para>封装 LibVLCSharp.Shared.MediaPlayer，纯音频模式（无视频输出）。</para>
/// <para>循环播放通过 EndReached 事件重新 Play() 实现。</para>
/// <para>音量映射：IAudioPlayer 0.0~1.0 → LibVLCSharp 0~100。</para>
/// </summary>
public sealed class LibVlcAudioPlayer : IAudioPlayer
{
    private readonly LibVLC _libVLC;
    private LibVLCSharp.Shared.MediaPlayer? _player;
    private LibVLCSharp.Shared.Media? _media;
    private PlaybackState _state = PlaybackState.Stopped;
    private float _volume = 1.0f;
    private bool _muted;
    private bool _loop;
    private bool _disposed;

    /// <summary>当前 PlayAsync 调用的 TCS——每次 PlayAsync 创建新实例</summary>
    private TaskCompletionSource? _playTcs;

    /// <summary>当前注册的 EndReached 处理器——用于正确反注册</summary>
    private EventHandler<EventArgs>? _endReachedHandler;

    /// <summary>
    /// 构造播放器
    /// </summary>
    /// <param name="libVLC">全局 LibVLC 实例（由 LibVlcInitializer 提供）</param>
    /// <param name="loop">是否循环播放</param>
    public LibVlcAudioPlayer(LibVLC libVLC, bool loop = false)
    {
        _libVLC = libVLC;
        _loop = loop;
    }

    /// <inheritdoc/>
    public Task LoadAsync(string source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 防御性清理：若已有旧实例则先释放（避免资源泄漏）
        if (_player != null)
        {
            RemoveEndReachedHandler();
            _player.Stop();
            _player.Dispose();
            _player = null;
        }
        _media?.Dispose();
        _media = null;

        _media = new LibVLCSharp.Shared.Media(_libVLC, new Uri(source));
        _player = new LibVLCSharp.Shared.MediaPlayer(_libVLC)
        {
            Media = _media,
            Volume = VolumeToInt(_volume)
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task PlayAsync(CancellationToken ct = default)
    {
        if (_player == null) return;

        _state = PlaybackState.Playing;

        // 每次 PlayAsync 创建新的 TCS，避免旧 TCS 已完成导致立即返回
        _playTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // 注册 EndReached 处理器（先清理旧的）
        RemoveEndReachedHandler();
        _endReachedHandler = OnEndReached;
        _player.EndReached += _endReachedHandler;

        _player.Play();

        if (ct.CanBeCanceled)
        {
            // 可取消模式：注册取消回调
            await using var reg = ct.Register(() =>
            {
                _state = PlaybackState.Stopped;
                _playTcs.TrySetCanceled(ct);
            });
            try
            {
                await _playTcs.Task;
            }
            catch (OperationCanceledException)
            {
                _player.Stop();
                throw;
            }
        }
        else
        {
            // 不可取消模式——等待自然结束或 StopAsync
            await _playTcs.Task;
        }

        // 清理 EndReached 处理器
        RemoveEndReachedHandler();
    }

    /// <inheritdoc/>
    public Task PauseAsync(CancellationToken ct = default)
    {
        if (_player == null) return Task.CompletedTask;
        _player.SetPause(true);
        _state = PlaybackState.Paused;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct = default)
    {
        if (_player == null) return Task.CompletedTask;
        _player.Stop();
        _state = PlaybackState.Stopped;
        _playTcs?.TrySetResult();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SeekAsync(TimeSpan position, CancellationToken ct = default)
    {
        if (_player == null) return Task.CompletedTask;
        _player.SeekTo(position);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<TimeSpan> GetPositionAsync()
    {
        if (_player == null) return ValueTask.FromResult(TimeSpan.Zero);
        return ValueTask.FromResult(TimeSpan.FromMilliseconds(_player.Time));
    }

    /// <inheritdoc/>
    public ValueTask<TimeSpan> GetDurationAsync()
    {
        if (_media == null) return ValueTask.FromResult(TimeSpan.Zero);
        return ValueTask.FromResult(TimeSpan.FromMilliseconds(_media.Duration > 0 ? _media.Duration : 0));
    }

    /// <inheritdoc/>
    public ValueTask SetVolumeAsync(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        if (_player != null && !_muted)
            _player.Volume = VolumeToInt(_volume);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask SetMuteAsync(bool mute)
    {
        _muted = mute;
        if (_player != null)
            _player.Volume = mute ? 0 : VolumeToInt(_volume);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask<PlaybackState> GetStateAsync() => ValueTask.FromResult(_state);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        if (_player != null)
        {
            RemoveEndReachedHandler();
            _player.Stop();
            _player.Dispose();
            _player = null;
        }
        _media?.Dispose();
        _media = null;
        _playTcs?.TrySetResult();
        _state = PlaybackState.Finished;

        return ValueTask.CompletedTask;
    }

    // ========== 内部方法 ==========

    /// <summary>统一的 EndReached 处理器——循环模式重新播放，非循环模式完成 TCS</summary>
    private void OnEndReached(object? sender, EventArgs e)
    {
        if (_loop && _state == PlaybackState.Playing)
        {
            // 循环模式：重新播放
            _player?.Stop();
            _player?.Play();
        }
        else
        {
            _state = PlaybackState.Finished;
            _playTcs?.TrySetResult();
        }
    }

    /// <summary>安全移除当前 EndReached 处理器</summary>
    private void RemoveEndReachedHandler()
    {
        if (_player != null && _endReachedHandler != null)
        {
            _player.EndReached -= _endReachedHandler;
            _endReachedHandler = null;
        }
    }

    /// <summary>IAudioPlayer 0.0~1.0 → LibVLCSharp 0~100</summary>
    private static int VolumeToInt(float volume) => (int)(Math.Clamp(volume, 0f, 1f) * 100);
}
