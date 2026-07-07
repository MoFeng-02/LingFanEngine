namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 偏好设置服务接口
/// <para>统一管理玩家偏好（音量/文字速度/自动延迟/全屏等）。</para>
/// <para>所有偏好值存储在状态容器中（__pref_* 前缀），通过 SaveSystemState 持久化。</para>
/// </summary>
public interface IPreferencesService
{
    /// <summary>主控音量（0~1）</summary>
    float MasterVolume { get; set; }

    /// <summary>BGM 音量（0~1）</summary>
    float BgmVolume { get; set; }

    /// <summary>音效音量（0~1）</summary>
    float SeVolume { get; set; }

    /// <summary>语音音量（0~1）</summary>
    float VoiceVolume { get; set; }

    /// <summary>全局静音</summary>
    bool MasterMuted { get; set; }

    /// <summary>打字机速度（字符/秒）</summary>
    double TextSpeed { get; set; }

    /// <summary>自动模式延迟（秒）</summary>
    double AutoForwardDelay { get; set; }

    /// <summary>是否跳过未读文本</summary>
    bool SkipUnseen { get; set; }

    /// <summary>是否全屏</summary>
    bool Fullscreen { get; set; }
}
