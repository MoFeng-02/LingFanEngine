using System;
using System.Threading;
using System.Threading.Tasks;

namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// 音频管理器接口
/// <para>管理 BGM、SE、Voice 三个通道的音频播放。</para>
/// </summary>
public interface IAudioManager : IDisposable
{
    /// <summary>主控音量（0~1）</summary>
    float MasterVolume { get; set; }

    /// <summary>全局静音</summary>
    bool MasterMuted { get; set; }

    /// <summary>BGM 音量</summary>
    float BgmVolume { get; set; }

    /// <summary>SE 音量</summary>
    float SeVolume { get; set; }

    /// <summary>语音音量</summary>
    float VoiceVolume { get; set; }

    /// <summary>播放 BGM</summary>
    void PlayBgm(string filePath, float volume = 0.8f, bool loop = true);

    /// <summary>播放 BGM（异步）</summary>
    Task PlayBgmAsync(string filePath, float volume = 0.8f, bool loop = true);

    /// <summary>BGM 交叉淡入队列</summary>
    Task QueueBgmAsync(string path, float volume, double crossFadeDuration);

    /// <summary>停止所有 BGM</summary>
    Task StopBgmAsync();

    /// <summary>停止所有 SE</summary>
    Task StopSeAsync();

    /// <summary>播放音效</summary>
    void PlaySe(string filePath, float volume = 1.0f);

    /// <summary>播放环境音（独立通道，循环播放）</summary>
    void PlayAmbient(string filePath, float volume = 0.8f, bool loop = true);

    /// <summary>停止环境音</summary>
    Task StopAmbientAsync();

    /// <summary>播放语音</summary>
    void PlayVoice(string filePath, float volume = 1.0f);

    /// <summary>停止语音</summary>
    void StopVoice();

    /// <summary>暂停所有音频</summary>
    Task PauseAllAsync();

    /// <summary>恢复所有音频</summary>
    Task ResumeAllAsync();

    /// <summary>停止所有音频</summary>
    Task StopAllAsync();
}
