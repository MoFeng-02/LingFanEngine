using Avalonia.Controls;
using Avalonia.Media;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

/// <summary>
/// 视频呈现器——同步 __video_* 状态键到 GpuMediaPlayer 控件，管理视频播放器生命周期。
/// <para>状态驱动模式：VideoManager 写状态键 → VideoPresenter 读状态键渲染</para>
/// <para>音视频分离架构：GpuMediaPlayer 永久静音（Volume=0），视频纯视觉，音频走 AudioManager</para>
/// </summary>
internal sealed class VideoPresenter : IVideoPresenter
{
    private readonly IStateContainer _state;
    private readonly IEncryptedFileReader? _fileReader;

    private Panel? _sceneRoot;
    private Grid? _outerGrid;

    private MediaPlayer.Controls.GpuMediaPlayer? _videoPlayer;
    private string _lastVideoPath = "";
    /// <summary>当前播放用的实际路径（可能是临时解密文件）</summary>
    private string _currentPlayPath = "";
    /// <summary>当前是否使用临时文件</summary>
    private bool _currentIsTemp;
    private bool _lastVideoIsPlaying;
    private double _lastVideoPosition = -1;
    private double _lastVideoDuration = -1;
    private bool _lastVideoFinished;

    private Border? _cutsceneMask;

    public VideoPresenter(IStateContainer state, IEncryptedFileReader? fileReader = null)
    {
        _state = state;
        _fileReader = fileReader;
    }

    public void Attach(Panel? sceneRoot, Grid? outerGrid)
    {
        _sceneRoot = sceneRoot;
        _outerGrid = outerGrid;
    }

    public void Detach()
    {
        RemoveVideoPlayerFromTree();
        if (_cutsceneMask != null)
        {
            _cutsceneMask.IsVisible = false;
            _outerGrid?.Children.Remove(_cutsceneMask);
            _cutsceneMask = null;
        }
        _sceneRoot = null;
        _outerGrid = null;
    }

    public void Update()
    {
        var videoPath = _state.Get<string>(StateKeys.Video.CurrentPath) ?? "";
        var cutsceneActive = _state.Get<bool>(StateKeys.Video.CutsceneActive);

        // 路径变化 → 重建播放器
        if (videoPath != _lastVideoPath)
        {
            RemoveVideoPlayerFromTree();
            _lastVideoPath = videoPath;

            if (!string.IsNullOrEmpty(videoPath))
            {
                // Phase 50：即解即用——加密视频解密到临时文件
                var (playPath, isTemp) = _fileReader != null
                    ? _fileReader.TryDecryptToFile(videoPath)
                    : (videoPath, false);
                _currentPlayPath = playPath;
                _currentIsTemp = isTemp;

                _videoPlayer = new MediaPlayer.Controls.GpuMediaPlayer
                {
                    AutoPlay = _state.Get<bool>(StateKeys.Video.AutoPlay),
                    Volume = 0,
                    ZIndex = cutsceneActive ? 100 : 0,
                };

                try
                {
                    _videoPlayer.Source = new Uri(System.IO.Path.GetFullPath(playPath));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VideoPresenter] 视频路径无效: {ex.Message}");
                }

                _sceneRoot?.Children.Add(_videoPlayer);
            }
            return;
        }

        if (_videoPlayer == null || string.IsNullOrEmpty(videoPath))
        {
            var skipable = _state.Get<bool>(StateKeys.Video.CutsceneSkipable);
            UpdateCutsceneMask(cutsceneActive, skipable);
            return;
        }

        // 同步 ZIndex（过场模式覆盖一切）
        _videoPlayer.ZIndex = cutsceneActive ? 100 : 0;

        // 检测 IsFinished 被外部重置
        var currentIsFinished = _state.Get<bool>(StateKeys.Video.IsFinished);
        if (!currentIsFinished && _lastVideoFinished)
        {
            _lastVideoFinished = false;
        }

        // 同步播放/暂停状态
        var isPlaying = _state.Get<bool>(StateKeys.Video.IsPlaying);
        var isPaused = _state.Get<bool>(StateKeys.Video.IsPaused);
        var shouldPlay = isPlaying && !isPaused;

        if (shouldPlay != _lastVideoIsPlaying)
        {
            if (shouldPlay)
                _videoPlayer.Play();
            else if (isPaused)
                _videoPlayer.Pause();
            _lastVideoIsPlaying = shouldPlay;
        }

        // 处理跳转
        var seekPos = _state.Get<double?>(StateKeys.Video.SeekPosition);
        if (seekPos.HasValue)
        {
            _videoPlayer.Seek(TimeSpan.FromSeconds(seekPos.Value));
            _state.Set<object?>(StateKeys.Video.SeekPosition, null);
        }

        // 回写位置和时长
        var currentPos = _videoPlayer.Position.TotalSeconds;
        var currentDur = _videoPlayer.Duration.TotalSeconds;
        if (Math.Abs(currentPos - _lastVideoPosition) > 0.05)
        {
            _state.Set(StateKeys.Video.Position, currentPos);
            _lastVideoPosition = currentPos;
        }
        if (Math.Abs(currentDur - _lastVideoDuration) > 0.05)
        {
            _state.Set(StateKeys.Video.Duration, currentDur);
            _lastVideoDuration = currentDur;
        }

        // 播放结束检测
        if (currentDur > 0 && currentPos >= currentDur - 0.15 && shouldPlay && !_lastVideoFinished)
        {
            var loop = _state.Get<bool>(StateKeys.Video.Loop);
            if (loop)
            {
                _videoPlayer.Seek(TimeSpan.Zero);
            }
            else
            {
                _state.Set(StateKeys.Video.IsPlaying, false);
                _state.Set(StateKeys.Video.IsFinished, true);
                _lastVideoIsPlaying = false;
                _lastVideoFinished = true;

                if (cutsceneActive)
                {
                    _state.Set(StateKeys.Video.CutsceneActive, false);
                }
            }
        }

        // 过场遮罩更新
        var cutsceneSkipable = _state.Get<bool>(StateKeys.Video.CutsceneSkipable);
        UpdateCutsceneMask(cutsceneActive, cutsceneSkipable);
    }

    private void RemoveVideoPlayerFromTree()
    {
        if (_videoPlayer != null)
        {
            _videoPlayer.Stop();
            _sceneRoot?.Children.Remove(_videoPlayer);
            _videoPlayer = null;
        }
        // Phase 50：释放临时解密文件
        if (_currentIsTemp && !string.IsNullOrEmpty(_currentPlayPath))
        {
            _fileReader?.ReleaseTempFile(_currentPlayPath, _currentIsTemp);
        }
        _currentPlayPath = "";
        _currentIsTemp = false;
        _lastVideoPath = "";
        _lastVideoIsPlaying = false;
        _lastVideoPosition = -1;
        _lastVideoDuration = -1;
        _lastVideoFinished = false;
    }

    /// <summary>
    /// 更新过场动画遮罩
    /// <para>过场模式 + skipable=true 时显示透明全屏遮罩（ZIndex=101），拦截点击用于跳过</para>
    /// </summary>
    private void UpdateCutsceneMask(bool cutsceneActive, bool skipable)
    {
        if (!cutsceneActive || !skipable)
        {
            if (_cutsceneMask != null)
                _cutsceneMask.IsVisible = false;
            return;
        }

        if (_cutsceneMask == null)
        {
            _cutsceneMask = new Border
            {
                Background = Brushes.Transparent,
                ZIndex = 101,
                IsHitTestVisible = true,
            };
            _cutsceneMask.PointerPressed += (_, _) =>
            {
                _state.Set(StateKeys.Video.CutsceneSkipped, true);
                _state.Set(StateKeys.Video.CutsceneActive, false);
                _state.Set(StateKeys.Video.IsPlaying, false);
                _state.Set(StateKeys.Video.IsFinished, true);
            };
            _outerGrid?.Children.Add(_cutsceneMask);
        }

        _cutsceneMask.IsVisible = true;
    }
}
