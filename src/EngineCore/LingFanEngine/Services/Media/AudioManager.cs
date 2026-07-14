﻿﻿﻿﻿﻿﻿﻿﻿using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// 音频管理器
/// <para>管理 BGM、SE、Voice 三个通道的音频播放。</para>
/// <para>使用环境检测选择后端：Windows 用 WASAPI，Linux 用 SDL2，macOS 用 AVFoundation。</para>
/// </summary>
public class AudioManager : IAudioManager
{
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    private readonly Func<IAudioPlayer> _playerFactory;
    private readonly IEncryptedFileReader? _fileReader;
    private readonly object _audioLock = new();
    /// <summary>全局 BGM 循环取消源（StopBgmAsync 时触发，让所有 BGM 循环退出）</summary>
    private CancellationTokenSource _loopCts = new();
    // BGM 通道（多个播放器，支持重叠/混音）
    private readonly List<IAudioPlayer> _bgmPlayers = [];
    // 音效通道
    private readonly List<IAudioPlayer> _sePlayers = [];
    // 语音通道
    private IAudioPlayer? _voicePlayer;
    // 环境音通道（单循环播放器）
    private IAudioPlayer? _ambientPlayer;
    private CancellationTokenSource _ambientCts = new();

    /// <summary>
    /// 主控音量（Master Volume，所有通道的全局乘数，0~1）
    /// </summary>
    private float _masterVolume = 1.0f;
    public float MasterVolume
    {
        get => _masterVolume;
        set { _masterVolume = value; UpdateActivePlayerVolumes(); }
    }

    /// <summary>
    /// 全局静音
    /// </summary>
    private bool _masterMuted;
    public bool MasterMuted
    {
        get => _masterMuted;
        set { _masterMuted = value; UpdateActivePlayerVolumes(); }
    }

    private void UpdateActivePlayerVolumes()
    {
        var effective = MasterMuted ? 0 : _masterVolume;
        IAudioPlayer? voice;
        IAudioPlayer? ambient;
        lock (_audioLock)
        {
            foreach (var p in _bgmPlayers) _ = p.SetVolumeAsync(effective * BgmVolume);
            foreach (var p in _sePlayers) _ = p.SetVolumeAsync(effective * SeVolume);
            voice = _voicePlayer;
            ambient = _ambientPlayer;
        }
        if (voice != null) _ = voice.SetVolumeAsync(effective * VoiceVolume);
        if (ambient != null) _ = ambient.SetVolumeAsync(effective * BgmVolume);
    }

    /// <summary>
    /// 当前 BGM 音量
    /// </summary>
    public float BgmVolume { get; set; } = 0.8f;

    /// <summary>
    /// 当前 SE 音量
    /// </summary>
    public float SeVolume { get; set; } = 1.0f;

    /// <summary>
    /// 当前语音音量
    /// </summary>
    public float VoiceVolume { get; set; } = 1.0f;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="playerFactory">可选：自定义播放器工厂。不提供时使用 NullAsyncAudioPlayer（空操作）。</param>
    /// <param name="fileReader">可选：加密文件读取器。Phase 50 即解即用——加密音频自动解密到临时文件。</param>
    public AudioManager(ICommandPipeline pipeline, IStateContainer state,
        Func<IAudioPlayer>? playerFactory = null, IEncryptedFileReader? fileReader = null)
    {
        _pipeline = pipeline;
        _state = state;
        _playerFactory = playerFactory ?? (() => new NullAsyncAudioPlayer());
        _fileReader = fileReader;
    }

    /// <summary>
    /// 解析音频文件路径：加密文件解密到临时文件，未加密直接返回原始路径。
    /// </summary>
    /// <returns>(播放路径, 是否为临时文件)</returns>
    private (string path, bool isTemp) ResolveAudioPath(string filePath)
    {
        if (_fileReader == null) return (filePath, false);
        return _fileReader.TryDecryptToFile(filePath);
    }

    private float EffectiveVolume(float groupVol) => MasterMuted ? 0 : groupVol * MasterVolume;

    /// <summary>
    /// 播放 BGM
    /// </summary>
    public void PlayBgm(string filePath, float volume = 0.8f, bool loop = true)
    {
        var player = CreatePlayer();
        _ = LoadAndPlayBgmAsync(player, filePath, EffectiveVolume(volume), loop);
    }

    public async Task PlayBgmAsync(string filePath, float volume = 0.8f, bool loop = true)
    {
        var player = CreatePlayer();
        await LoadAndPlayBgmAsync(player, filePath, EffectiveVolume(volume), loop);
    }

    private async Task LoadAndPlayBgmAsync(IAudioPlayer player, string filePath, float volume, bool loop)
    {
        var (playPath, isTemp) = ResolveAudioPath(filePath);
        try
        {
            await player.LoadAsync(playPath);
            await player.SetVolumeAsync(volume);
            lock (_audioLock) _bgmPlayers.Add(player);
            _state.Set(StateKeys.Audio.BgmPath, filePath);

            var cts = _loopCts;
            do
            {
                await player.PlayAsync(cts.Token);
            }
            while (loop && !cts.IsCancellationRequested && IsPlayerActive(player));

            lock (_audioLock) { _bgmPlayers.Remove(player); }
            // player Dispose 由 finally 统一处理
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] BGM error: {ex.Message}");
        }
        finally
        {
            try { await player.DisposeAsync(); } catch { }
            _fileReader?.ReleaseTempFile(playPath, isTemp);
        }
    }

    /// <summary>
    /// BGM 交叉淡入队列：先停旧 BGM，再播新 BGM
    /// </summary>
    public async Task QueueBgmAsync(string path, float volume, double crossFadeDuration)
    {
        await StopBgmAsync();
        PlayBgm(path, volume);
    }

    /// <summary>
    /// 停止所有 BGM
    /// </summary>
    public async Task StopBgmAsync()
    {
        List<IAudioPlayer> players;
        lock (_audioLock)
        {
            players = [.. _bgmPlayers];
            _bgmPlayers.Clear();
        }
        _loopCts.Cancel();
        // P1-#19: Dispose 旧 CTS 防止资源泄漏，再创建新的
        _loopCts.Dispose();
        _loopCts = new CancellationTokenSource();
        foreach (var p in players) { await p.StopAsync(); await p.DisposeAsync(); }
        _state.Set(StateKeys.Audio.BgmPath, "");
    }

    /// <summary>
    /// 停止所有 SE
    /// </summary>
    public async Task StopSeAsync()
    {
        List<IAudioPlayer> players;
        lock (_audioLock)
        {
            players = [.. _sePlayers];
            _sePlayers.Clear();
        }
        foreach (var p in players) { await p.StopAsync(); await p.DisposeAsync(); }
    }

    /// <summary>
    /// 播放音效
    /// </summary>
    public void PlaySe(string filePath, float volume = 1.0f)
    {
        var player = CreatePlayer();
        _ = LoadAndPlaySeAsync(player, filePath, EffectiveVolume(volume));
    }

    private async Task LoadAndPlaySeAsync(IAudioPlayer player, string filePath, float volume)
    {
        var (playPath, isTemp) = ResolveAudioPath(filePath);
        try
        {
            await player.LoadAsync(playPath);
            await player.SetVolumeAsync(volume);
            lock (_audioLock) _sePlayers.Add(player);
            await player.PlayAsync();
            lock (_audioLock) _sePlayers.Remove(player);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] SE error: {ex.Message}");
        }
        finally
        {
            try { await player.DisposeAsync(); } catch { }
            _fileReader?.ReleaseTempFile(playPath, isTemp);
        }
    }

    /// <summary>
    /// 播放环境音（独立通道，循环播放）
    /// </summary>
    public void PlayAmbient(string filePath, float volume = 0.8f, bool loop = true)
    {
        // 停止旧的环境音
        _ = StopAmbientAsync();
        var player = CreatePlayer();
        _ = LoadAndPlayAmbientAsync(player, filePath, EffectiveVolume(volume), loop);
    }

    private async Task LoadAndPlayAmbientAsync(IAudioPlayer player, string filePath, float volume, bool loop)
    {
        var (playPath, isTemp) = ResolveAudioPath(filePath);
        try
        {
            await player.LoadAsync(playPath);
            await player.SetVolumeAsync(volume);
            lock (_audioLock) _ambientPlayer = player;
            _state.Set(StateKeys.Audio.AmbientPath, filePath);

            _ambientCts.Cancel();
            _ambientCts.Dispose();
            _ambientCts = new CancellationTokenSource();
            var cts = _ambientCts;

            do
            {
                await player.PlayAsync(cts.Token);
            }
            while (loop && !cts.IsCancellationRequested);

            lock (_audioLock) { if (_ambientPlayer == player) _ambientPlayer = null; }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Ambient error: {ex.Message}");
        }
        finally
        {
            try { await player.DisposeAsync(); } catch { }
            _fileReader?.ReleaseTempFile(playPath, isTemp);
        }
    }

    /// <summary>
    /// 停止环境音
    /// </summary>
    public async Task StopAmbientAsync()
    {
        IAudioPlayer? player;
        lock (_audioLock) { player = _ambientPlayer; _ambientPlayer = null; }
        _ambientCts.Cancel();
        _ambientCts.Dispose();
        _ambientCts = new CancellationTokenSource();
        if (player != null)
        {
            try { await player.StopAsync(); } catch { }
            await player.DisposeAsync();
        }
        _state.Set(StateKeys.Audio.AmbientPath, "");
    }

    /// <summary>
    /// 播放语音
    /// </summary>
    public void PlayVoice(string filePath, float volume = 1.0f)
    {
        IAudioPlayer? oldPlayer;
        lock (_audioLock) { oldPlayer = _voicePlayer; _voicePlayer = null; }
        var player = CreatePlayer();
        lock (_audioLock) _voicePlayer = player;
        _ = StopAndPlayVoiceAsync(oldPlayer, player, filePath, EffectiveVolume(volume));
    }

    private async Task StopAndPlayVoiceAsync(IAudioPlayer? old, IAudioPlayer player, string filePath, float volume)
    {
        if (old != null) { try { await old.StopAsync(); } catch { } await old.DisposeAsync(); }
        await LoadAndPlayVoiceAsync(player, filePath, volume);
    }

    private async Task LoadAndPlayVoiceAsync(IAudioPlayer player, string filePath, float volume)
    {
        var (playPath, isTemp) = ResolveAudioPath(filePath);
        try
        {
            await player.LoadAsync(playPath);
            await player.SetVolumeAsync(volume);
            await player.PlayAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Voice error: {ex.Message}");
        }
        finally
        {
            try { await player.DisposeAsync(); } catch { }
            _fileReader?.ReleaseTempFile(playPath, isTemp);
        }
    }

    /// <summary>
    /// 停止语音
    /// </summary>
    public void StopVoice()
    {
        IAudioPlayer? player;
        lock (_audioLock) { player = _voicePlayer; _voicePlayer = null; }
        _ = StopAndDisposeAsync(player);
    }

    private static async Task StopAndDisposeAsync(IAudioPlayer? player)
    {
        if (player == null) return;
        try { await player.StopAsync(); }
        catch { }
        await player.DisposeAsync();
    }

    /// <summary>
    /// 暂停所有音频
    /// </summary>
    public async Task PauseAllAsync()
    {
        List<IAudioPlayer> bgmPlayers, sePlayers;
        lock (_audioLock)
        {
            bgmPlayers = [.. _bgmPlayers];
            sePlayers = [.. _sePlayers];
        }
        foreach (var bgm in bgmPlayers) await bgm.PauseAsync();
        IAudioPlayer? voice1; lock (_audioLock) voice1 = _voicePlayer;
        if (voice1 != null) await voice1.PauseAsync();
        foreach (var se in sePlayers) await se.PauseAsync();
    }

    /// <summary>
    /// 恢复所有音频
    /// </summary>
    public async Task ResumeAllAsync()
    {
        List<IAudioPlayer> bgmPlayers, sePlayers;
        lock (_audioLock)
        {
            bgmPlayers = [.. _bgmPlayers];
            sePlayers = [.. _sePlayers];
        }
        IAudioPlayer? voice2; lock (_audioLock) voice2 = _voicePlayer;
        foreach (var bgm in bgmPlayers) _ = bgm.PlayAsync();
        if (voice2 != null) _ = voice2.PlayAsync();
        foreach (var se in sePlayers) _ = se.PlayAsync();
    }

    /// <summary>
    /// 停止所有音频
    /// </summary>
    public async Task StopAllAsync()
    {
        await StopBgmAsync();
        await StopAmbientAsync();
        StopVoice();
        List<IAudioPlayer> sePlayers;
        lock (_audioLock)
        {
            sePlayers = [.. _sePlayers];
            _sePlayers.Clear();
        }
        foreach (var se in sePlayers) { await se.StopAsync(); await se.DisposeAsync(); }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _ = StopAllAsync();
    }

    private bool IsPlayerActive(IAudioPlayer player)
    {
        lock (_audioLock) return _bgmPlayers.Contains(player);
    }

    private IAudioPlayer CreatePlayer() => _playerFactory();
}