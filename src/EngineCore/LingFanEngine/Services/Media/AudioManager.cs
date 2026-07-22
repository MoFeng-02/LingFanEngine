﻿using System.Collections.Concurrent;
using System.Collections.Immutable;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// 音频管理器
/// <para>管理 BGM、SE、Voice、Ambient 四个通道的音频播放。</para>
/// <para>使用环境检测选择后端：Windows 用 WASAPI，Linux 用 SDL2，macOS 用 AVFoundation。</para>
/// <para>Phase 64：无锁设计——ConcurrentDictionary + ImmutableArray 原子替换。</para>
/// </summary>
public class AudioManager : IAudioManager
{
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    private readonly Func<IAudioPlayer> _playerFactory;
    private readonly IEncryptedFileReader? _fileReader;

    /// <summary>BGM 播放器列表（不可变数组，原子替换）</summary>
    private ImmutableArray<IAudioPlayer> _bgmPlayers = ImmutableArray<IAudioPlayer>.Empty;
    /// <summary>SE 播放器列表（不可变数组，原子替换）</summary>
    private ImmutableArray<IAudioPlayer> _sePlayers = ImmutableArray<IAudioPlayer>.Empty;
    /// <summary>Voice 播放器（原子替换）</summary>
    private IAudioPlayer? _voicePlayer;
    /// <summary>Ambient 播放器（原子替换）</summary>
    private IAudioPlayer? _ambientPlayer;

    /// <summary>全局 BGM 循环取消源（StopBgmAsync 时触发，让所有 BGM 循环退出）</summary>
    private CancellationTokenSource _loopCts = new();
    /// <summary>Ambient 循环取消源</summary>
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
        // 拍快照——ImmutableArray 不可变，无需加锁
        var bgm = _bgmPlayers;
        var se = _sePlayers;
        var voice = _voicePlayer;
        var ambient = _ambientPlayer;

        foreach (var p in bgm) FireAndForgetVolume(p, effective * BgmVolume);
        foreach (var p in se) FireAndForgetVolume(p, effective * SeVolume);
        if (voice != null) FireAndForgetVolume(voice, effective * VoiceVolume);
        if (ambient != null) FireAndForgetVolume(ambient, effective * BgmVolume);
    }

    /// <summary>fire-and-forget SetVolumeAsync 带异常捕获</summary>
    private static void FireAndForgetVolume(IAudioPlayer player, float volume)
    {
        _ = player.SetVolumeAsync(volume).AsTask().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[Audio] SetVolumeAsync failed: {t.Exception?.GetBaseException().Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
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
        bool cancelledByStop = false;
        try
        {
            await player.LoadAsync(playPath);
            await player.SetVolumeAsync(volume);
            // 原子添加到 BGM 列表
            ImmutableInterlocked.Update(ref _bgmPlayers, arr => arr.Add(player));
            // 拍快照——后续 StopBgmAsync 会替换 _loopCts，但当前 Token 不变
            var cts = _loopCts.Token;
            _state.Set(StateKeys.Audio.BgmPath, filePath);

            do
            {
                await player.PlayAsync(cts);
            }
            while (loop && !cts.IsCancellationRequested && IsPlayerActive(player));

            // 若因 CTS 取消退出，说明 StopBgmAsync 触发——由其负责 Dispose
            cancelledByStop = cts.IsCancellationRequested;

            // 原子移除
            ImmutableInterlocked.Update(ref _bgmPlayers, arr => arr.Remove(player));
        }
        catch (OperationCanceledException)
        {
            // StopBgmAsync 的 CTS 取消——由其负责 Dispose
            cancelledByStop = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] BGM error: {ex.Message}");
        }
        finally
        {
            // 仅在非取消退出时自行 Dispose；取消退出时由 StopBgmAsync 负责 Dispose
            if (!cancelledByStop)
            {
                try { await player.DisposeAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] BGM DisposeAsync failed: {ex.Message}"); }
            }
            _fileReader?.ReleaseTempFile(playPath, isTemp);
        }
    }

    /// <summary>
    /// BGM 交叉淡入队列：旧 BGM 淡出后切换到新 BGM
    /// <para>简化版 crossfade：先逐步降低旧 BGM 音量到 0，再 stop + play 新 BGM。</para>
    /// <para>真正的并行交叉淡入淡出需要每 player 独立 CTS（后续可迭代）。</para>
    /// </summary>
    public async Task QueueBgmAsync(string path, float volume, double crossFadeDuration)
    {
        if (crossFadeDuration <= 0)
        {
            // 无渐变——直接 stop + play
            await StopBgmAsync();
            PlayBgm(path, volume);
            return;
        }

        // 拍快照旧 BGM players
        var oldPlayers = _bgmPlayers;
        var currentBgmVol = EffectiveVolume(BgmVolume);

        // 淡出旧 BGM（逐步降低音量到 0）
        var steps = 10;
        var stepDelayMs = (int)(crossFadeDuration * 1000 / steps);
        for (int i = steps - 1; i >= 0; i--)
        {
            var factor = (float)i / steps;
            foreach (var p in oldPlayers)
            {
                try { await p.SetVolumeAsync(currentBgmVol * factor); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] QueueBgm fade-out SetVolumeAsync failed: {ex.Message}"); }
            }
            await Task.Delay(stepDelayMs);
        }

        // 淡出完成，stop 旧 BGM
        await StopBgmAsync();

        // 播放新 BGM（以目标音量开始）
        PlayBgm(path, volume);
    }

    /// <summary>
    /// 停止所有 BGM
    /// </summary>
    public async Task StopBgmAsync()
    {
        // 原子清空并取回旧快照（Update 在并发重试时会多次赋值 players，
        // 但 StopBgmAsync 极少并发，未处理的 player 会在循环结束时自行 Dispose）
        ImmutableArray<IAudioPlayer> players = default;
        ImmutableInterlocked.Update(ref _bgmPlayers, arr =>
        {
            players = arr;
            return ImmutableArray<IAudioPlayer>.Empty;
        });

        // 原子替换 _loopCts——先 Cancel 旧 Token 让所有循环退出，再 Dispose
        var oldCts = Interlocked.Exchange(ref _loopCts, new CancellationTokenSource());
        oldCts.Cancel();
        oldCts.Dispose();

        foreach (var p in players) { await p.StopAsync(); await p.DisposeAsync(); }
        _state.Set(StateKeys.Audio.BgmPath, "");
    }

    /// <summary>
    /// 停止所有 SE
    /// </summary>
    public async Task StopSeAsync()
    {
        ImmutableArray<IAudioPlayer> players = default;
        ImmutableInterlocked.Update(ref _sePlayers, arr =>
        {
            players = arr;
            return ImmutableArray<IAudioPlayer>.Empty;
        });
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
        bool disposedByStop = false;
        try
        {
            await player.LoadAsync(playPath);
            await player.SetVolumeAsync(volume);
            ImmutableInterlocked.Update(ref _sePlayers, arr => arr.Add(player));
            await player.PlayAsync();
            // 自然结束后移除——若已被 StopSeAsync 原子清空，Contains 返回 false → 由其负责 Dispose
            if (_sePlayers.Contains(player))
            {
                ImmutableInterlocked.Update(ref _sePlayers, arr => arr.Remove(player));
            }
            else
            {
                disposedByStop = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] SE error: {ex.Message}");
            // 异常退出时也检查——若已被 StopSeAsync 移除则由其负责 Dispose
            if (!_sePlayers.Contains(player))
                disposedByStop = true;
        }
        finally
        {
            // 仅在非 StopSeAsync 取消时自行 Dispose；取消退出时由 StopSeAsync 负责
            if (!disposedByStop)
            {
                try { await player.DisposeAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] SE DisposeAsync failed: {ex.Message}"); }
            }
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

            // 原子替换 _ambientPlayer（取回旧 player 供 StopAmbientAsync 处理）
            var oldPlayer = Interlocked.Exchange(ref _ambientPlayer, player);

            // 原子替换 _ambientCts
            var oldCts = Interlocked.Exchange(ref _ambientCts, new CancellationTokenSource());
            oldCts.Cancel();
            oldCts.Dispose();

            // 拍快照
            var cts = _ambientCts.Token;
            _state.Set(StateKeys.Audio.AmbientPath, filePath);

            do
            {
                await player.PlayAsync(cts);
            }
            while (loop && !cts.IsCancellationRequested);

            // 清除引用（仅当仍是当前 player 时）
            Interlocked.CompareExchange(ref _ambientPlayer, null, player);

            // 释放旧 ambient player
            if (oldPlayer != null)
            {
                try { await oldPlayer.StopAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] Ambient old StopAsync failed: {ex.Message}"); }
                await oldPlayer.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Audio] Ambient error: {ex.Message}");
        }
        finally
        {
            try { await player.DisposeAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] Ambient DisposeAsync failed: {ex.Message}"); }
            _fileReader?.ReleaseTempFile(playPath, isTemp);
        }
    }

    /// <summary>
    /// 停止环境音
    /// </summary>
    public async Task StopAmbientAsync()
    {
        // 原子取出并清空
        var player = Interlocked.Exchange(ref _ambientPlayer, null);

        // 原子替换 _ambientCts
        var oldCts = Interlocked.Exchange(ref _ambientCts, new CancellationTokenSource());
        oldCts.Cancel();
        oldCts.Dispose();

        if (player != null)
        {
            try { await player.StopAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] StopAmbient StopAsync failed: {ex.Message}"); }
            await player.DisposeAsync();
        }
        _state.Set(StateKeys.Audio.AmbientPath, "");
    }

    /// <summary>
    /// 播放语音
    /// </summary>
    public void PlayVoice(string filePath, float volume = 1.0f)
    {
        var oldPlayer = Interlocked.Exchange(ref _voicePlayer, null);
        var player = CreatePlayer();
        Interlocked.Exchange(ref _voicePlayer, player);
        _ = StopAndPlayVoiceAsync(oldPlayer, player, filePath, EffectiveVolume(volume));
    }

    private async Task StopAndPlayVoiceAsync(IAudioPlayer? old, IAudioPlayer player, string filePath, float volume)
    {
        if (old != null) { try { await old.StopAsync(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] Voice old StopAsync failed: {ex.Message}"); } await old.DisposeAsync(); }
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
            try { await player.DisposeAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] Voice DisposeAsync failed: {ex.Message}"); }
            _fileReader?.ReleaseTempFile(playPath, isTemp);
        }
    }

    /// <summary>
    /// 停止语音
    /// </summary>
    public void StopVoice()
    {
        var player = Interlocked.Exchange(ref _voicePlayer, null);
        _ = StopAndDisposeAsync(player);
    }

    private static async Task StopAndDisposeAsync(IAudioPlayer? player)
    {
        if (player == null) return;
        try { await player.StopAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audio] StopAndDispose StopAsync failed: {ex.Message}"); }
        await player.DisposeAsync();
    }

    /// <summary>
    /// 暂停所有音频
    /// </summary>
    public async Task PauseAllAsync()
    {
        // 拍快照
        var bgmPlayers = _bgmPlayers;
        var sePlayers = _sePlayers;
        var voice = _voicePlayer;
        foreach (var bgm in bgmPlayers) await bgm.PauseAsync();
        if (voice != null) await voice.PauseAsync();
        foreach (var se in sePlayers) await se.PauseAsync();
    }

    /// <summary>
    /// 恢复所有音频
    /// </summary>
    public async Task ResumeAllAsync()
    {
        // 拍快照
        var bgmPlayers = _bgmPlayers;
        var sePlayers = _sePlayers;
        var voice = _voicePlayer;
        // 使用 ResumeAsync 而非 PlayAsync——避免创建新 TCS 导致原 PlayAsync 的 Task 变孤儿
        foreach (var bgm in bgmPlayers) await bgm.ResumeAsync();
        if (voice != null) await voice.ResumeAsync();
        foreach (var se in sePlayers) await se.ResumeAsync();
    }

    /// <summary>
    /// 停止所有音频
    /// </summary>
    public async Task StopAllAsync()
    {
        await StopBgmAsync();
        await StopAmbientAsync();
        StopVoice();
        ImmutableArray<IAudioPlayer> sePlayers = default;
        ImmutableInterlocked.Update(ref _sePlayers, arr =>
        {
            sePlayers = arr;
            return ImmutableArray<IAudioPlayer>.Empty;
        });
        foreach (var se in sePlayers) { await se.StopAsync(); await se.DisposeAsync(); }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _ = StopAllAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[Audio] Dispose StopAllAsync failed: {t.Exception?.GetBaseException().Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private bool IsPlayerActive(IAudioPlayer player)
    {
        // ImmutableArray.Contains 线程安全
        return _bgmPlayers.Contains(player);
    }

    private IAudioPlayer CreatePlayer() => _playerFactory();
}
