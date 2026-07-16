using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// 视频管理器——通过 StateContainer 状态键驱动 SceneView 中的 GpuMediaPlayer 控件
/// <para>不直接持有 UI 控件，所有操作通过写入 __video_* 状态键完成。</para>
/// <para>SceneView 每帧读取这些状态键，创建/更新/销毁 GpuMediaPlayer 控件。</para>
/// <para>视频结束检测：SceneView 回写 IsFinished=true，VideoManager 每帧检查并触发 OnFinished 事件。</para>
/// </summary>
public class VideoManager : IVideoManager
{
    private readonly IStateContainer _state;
    private bool _lastIsFinished;

    public VideoManager(IStateContainer state)
    {
        _state = state;
    }

    /// <inheritdoc/>
    public float Volume
    {
        get => _state.Get<float>(StateKeys.Video.Volume);
        set => _state.Set(StateKeys.Video.Volume, Math.Clamp(value, 0f, 1f));
    }

    /// <inheritdoc/>
    public bool IsFinished => _state.Get<bool>(StateKeys.Video.IsFinished);

    /// <inheritdoc/>
    public event Action? OnFinished;

    /// <summary>
    /// 每帧由 GameLoop 调用，检查视频结束状态变化并触发事件
    /// </summary>
    public void PollFinished()
    {
        var current = _state.Get<bool>(StateKeys.Video.IsFinished);
        if (current && !_lastIsFinished)
        {
            OnFinished?.Invoke();
        }
        _lastIsFinished = current;
    }

    /// <inheritdoc/>
    public void Play(string path, float volume = 1.0f, bool loop = false, bool autoPlay = true)
    {
        // P1-#8: 清除可能残留的过场标记——防止上次过场动画的遮罩在新视频播放时仍显示
        _state.Set(StateKeys.Video.CutsceneActive, false);
        _state.Set(StateKeys.Video.CurrentPath, path);
        _state.Set(StateKeys.Video.Volume, Math.Clamp(volume, 0f, 1f));
        _state.Set(StateKeys.Video.Loop, loop);
        _state.Set(StateKeys.Video.AutoPlay, autoPlay);
        _state.Set(StateKeys.Video.IsPlaying, true);
        _state.Set(StateKeys.Video.IsPaused, false);
        _state.Set(StateKeys.Video.IsFinished, false);
        _state.Set<object?>(StateKeys.Video.SeekPosition, null);
        _lastIsFinished = false;
    }

    /// <inheritdoc/>
    public void Stop()
    {
        _state.Set(StateKeys.Video.IsPlaying, false);
        _state.Set(StateKeys.Video.IsPaused, false);
        _state.Set(StateKeys.Video.IsFinished, false);
        _state.Set(StateKeys.Video.CurrentPath, "");
        _state.Set<object?>(StateKeys.Video.SeekPosition, null);
        _state.Set(StateKeys.Video.CutsceneActive, false);
        _state.Set(StateKeys.Video.CutsceneSkipable, true);
    }

    /// <inheritdoc/>
    public void Pause()
    {
        _state.Set(StateKeys.Video.IsPlaying, false);
        _state.Set(StateKeys.Video.IsPaused, true);
    }

    /// <inheritdoc/>
    public void Resume()
    {
        _state.Set(StateKeys.Video.IsPlaying, true);
        _state.Set(StateKeys.Video.IsPaused, false);
    }

    /// <inheritdoc/>
    public void Seek(TimeSpan position)
    {
        _state.Set(StateKeys.Video.SeekPosition, position.TotalSeconds);
    }

    /// <inheritdoc/>
    public void PlayCutscene(string path, bool skipable = true, float volume = 1.0f)
    {
        _state.Set(StateKeys.Video.CutsceneActive, true);
        _state.Set(StateKeys.Video.CutsceneSkipped, false);
        _state.Set(StateKeys.Video.CutsceneSkipable, skipable);
        Play(path, volume, loop: false, autoPlay: true);
    }
}
