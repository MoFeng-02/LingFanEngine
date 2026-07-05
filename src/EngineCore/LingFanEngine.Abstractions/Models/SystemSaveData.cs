using System.Text.Json.Serialization;

namespace LingFanEngine.Abstractions.Models;

/// <summary>
/// 系统偏好存档（独立于游戏存档，跨会话持久化）
/// </summary>
public class SystemSaveData
{
    /// <summary>游戏版本（用于迁移检查）</summary>
    public string GameVersion { get; set; } = "1.0.0";

    /// <summary>上次退出时场景</summary>
    public string? LastScene { get; set; }

    /// <summary>记录时间</summary>
    public DateTimeOffset SaveTime { get; set; } = DateTimeOffset.UtcNow;

    // ═══ 可持久化的 __* 变量 ═══

    /// <summary>语言</summary>
    public string? Language { get; set; }

    /// <summary>主控音量</summary>
    public float MasterVolume { get; set; } = 1.0f;

    /// <summary>全局静音</summary>
    public bool MasterMuted { get; set; }

    /// <summary>BGM 音量</summary>
    public float BgmVolume { get; set; } = 0.8f;

    /// <summary>SE 音量</summary>
    public float SeVolume { get; set; } = 1.0f;

    /// <summary>语音音量</summary>
    public float VoiceVolume { get; set; } = 1.0f;

    /// <summary>默认打字机速度</summary>
    public double DefaultTextSpeed { get; set; } = 60;

    /// <summary>对话栏宽百分比</summary>
    public double? DialogWPercent { get; set; }

    /// <summary>对话栏高百分比</summary>
    public double? DialogHPercent { get; set; }

    /// <summary>主窗口宽度</summary>
    public int? WindowWidth { get; set; }

    /// <summary>主窗口高度</summary>
    public int? WindowHeight { get; set; }
}
