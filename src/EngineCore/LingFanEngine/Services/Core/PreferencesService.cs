using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 偏好设置服务——统一管理玩家偏好（音量/文字速度/自动延迟/全屏等）
/// <para>所有偏好值存储在状态容器中（__pref_* 前缀），通过 SaveSystemState 持久化。</para>
/// <para>对标 Ren'Py Preferences 对象。</para>
/// </summary>
public class PreferencesService : IPreferencesService
{
    private readonly IStateContainer _state;

    public PreferencesService(IStateContainer state)
    {
        _state = state;
        EnsureDefaults();
    }

    /// <summary>确保偏好设置有默认值</summary>
    private void EnsureDefaults()
    {
        if (!_state.ContainsKey(StateKeys.Preferences.MasterVolume))
            _state.Set(StateKeys.Preferences.MasterVolume, 1.0f);
        if (!_state.ContainsKey(StateKeys.Preferences.BgmVolume))
            _state.Set(StateKeys.Preferences.BgmVolume, 0.8f);
        if (!_state.ContainsKey(StateKeys.Preferences.SeVolume))
            _state.Set(StateKeys.Preferences.SeVolume, 0.6f);
        if (!_state.ContainsKey(StateKeys.Preferences.VoiceVolume))
            _state.Set(StateKeys.Preferences.VoiceVolume, 1.0f);
        if (!_state.ContainsKey(StateKeys.Preferences.MasterMuted))
            _state.Set(StateKeys.Preferences.MasterMuted, false);
        if (!_state.ContainsKey(StateKeys.Preferences.TextSpeed))
            _state.Set(StateKeys.Preferences.TextSpeed, 30.0);
        if (!_state.ContainsKey(StateKeys.Preferences.AutoForwardDelay))
            _state.Set(StateKeys.Preferences.AutoForwardDelay, 3.0);
        if (!_state.ContainsKey(StateKeys.Preferences.SkipUnseen))
            _state.Set(StateKeys.Preferences.SkipUnseen, false);
        if (!_state.ContainsKey(StateKeys.Preferences.Fullscreen))
            _state.Set(StateKeys.Preferences.Fullscreen, false);
    }

    // ========== 音量 ==========

    public float MasterVolume
    {
        get => _state.Get<float>(StateKeys.Preferences.MasterVolume);
        set => _state.Set(StateKeys.Preferences.MasterVolume, Math.Clamp(value, 0f, 1f));
    }

    public float BgmVolume
    {
        get => _state.Get<float>(StateKeys.Preferences.BgmVolume);
        set => _state.Set(StateKeys.Preferences.BgmVolume, Math.Clamp(value, 0f, 1f));
    }

    public float SeVolume
    {
        get => _state.Get<float>(StateKeys.Preferences.SeVolume);
        set => _state.Set(StateKeys.Preferences.SeVolume, Math.Clamp(value, 0f, 1f));
    }

    public float VoiceVolume
    {
        get => _state.Get<float>(StateKeys.Preferences.VoiceVolume);
        set => _state.Set(StateKeys.Preferences.VoiceVolume, Math.Clamp(value, 0f, 1f));
    }

    public bool MasterMuted
    {
        get => _state.Get<bool>(StateKeys.Preferences.MasterMuted);
        set => _state.Set(StateKeys.Preferences.MasterMuted, value);
    }

    /// <summary>获取有效音量（考虑静音）</summary>
    public float GetEffectiveVolume(string channel)
    {
        if (MasterMuted) return 0f;
        var vol = channel.ToLowerInvariant() switch
        {
            "master" => MasterVolume,
            "bgm" => BgmVolume * MasterVolume,
            "se" => SeVolume * MasterVolume,
            "voice" => VoiceVolume * MasterVolume,
            _ => MasterVolume
        };
        return vol;
    }

    // ========== 文字/播放 ==========

    public double TextSpeed
    {
        get => _state.Get<double>(StateKeys.Preferences.TextSpeed);
        set => _state.Set(StateKeys.Preferences.TextSpeed, Math.Max(1.0, value));
    }

    public double AutoForwardDelay
    {
        get => _state.Get<double>(StateKeys.Preferences.AutoForwardDelay);
        set
        {
            _state.Set(StateKeys.Preferences.AutoForwardDelay, value);
            // 同步到 Playback.AutoDelay
            _state.Set(StateKeys.Playback.AutoDelay, value);
        }
    }

    public bool SkipUnseen
    {
        get => _state.Get<bool>(StateKeys.Preferences.SkipUnseen);
        set => _state.Set(StateKeys.Preferences.SkipUnseen, value);
    }

    // ========== 显示 ==========

    public bool Fullscreen
    {
        get => _state.Get<bool>(StateKeys.Preferences.Fullscreen);
        set => _state.Set(StateKeys.Preferences.Fullscreen, value);
    }

    public string? Language
    {
        get => _state.Get<string>(StateKeys.Preferences.Language);
        set => _state.Set(StateKeys.Preferences.Language, value);
    }
}
