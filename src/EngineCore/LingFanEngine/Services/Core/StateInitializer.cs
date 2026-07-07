using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 状态初始化器实现
/// <para>在引擎启动时初始化所有默认状态键值（仅当键不存在时写入）。</para>
/// </summary>
public class StateInitializer : IStateInitializer
{
    /// <inheritdoc/>
    public void Initialize(IStateContainer state)
    {
        // 对话历史
        if (!state.ContainsKey(StateKeys.History.Entries))
            state.Set(StateKeys.History.Entries, new List<DialogHistoryEntry>());
        if (!state.ContainsKey(StateKeys.History.MaxCount))
            state.Set(StateKeys.History.MaxCount, 100);
        if (!state.ContainsKey(StateKeys.History.Visible))
            state.Set(StateKeys.History.Visible, false);

        // 跳过/自动模式
        if (!state.ContainsKey(StateKeys.Playback.SkipActive))
            state.Set(StateKeys.Playback.SkipActive, false);
        if (!state.ContainsKey(StateKeys.Playback.SkipOnlySeen))
            state.Set(StateKeys.Playback.SkipOnlySeen, false);
        if (!state.ContainsKey(StateKeys.Playback.AutoActive))
            state.Set(StateKeys.Playback.AutoActive, false);
        if (!state.ContainsKey(StateKeys.Playback.AutoDelay))
            state.Set(StateKeys.Playback.AutoDelay, 3.0);
        if (!state.ContainsKey(StateKeys.Playback.AutoTimer))
            state.Set(StateKeys.Playback.AutoTimer, 0.0);

        // 场景类型（默认 Game）
        if (!state.ContainsKey(StateKeys.Scene.CurrentType))
            state.Set(StateKeys.Scene.CurrentType, (int)Abstractions.Entities.Enums.SceneType.Game);

        // 偏好设置默认值
        if (!state.ContainsKey(StateKeys.Preferences.MasterVolume))
            state.Set(StateKeys.Preferences.MasterVolume, 1.0f);
        if (!state.ContainsKey(StateKeys.Preferences.BgmVolume))
            state.Set(StateKeys.Preferences.BgmVolume, 0.8f);
        if (!state.ContainsKey(StateKeys.Preferences.SeVolume))
            state.Set(StateKeys.Preferences.SeVolume, 0.6f);
        if (!state.ContainsKey(StateKeys.Preferences.VoiceVolume))
            state.Set(StateKeys.Preferences.VoiceVolume, 1.0f);
        if (!state.ContainsKey(StateKeys.Preferences.MasterMuted))
            state.Set(StateKeys.Preferences.MasterMuted, false);
        if (!state.ContainsKey(StateKeys.Preferences.TextSpeed))
            state.Set(StateKeys.Preferences.TextSpeed, 30.0);
        if (!state.ContainsKey(StateKeys.Preferences.AutoForwardDelay))
            state.Set(StateKeys.Preferences.AutoForwardDelay, 3.0);
        if (!state.ContainsKey(StateKeys.Preferences.SkipUnseen))
            state.Set(StateKeys.Preferences.SkipUnseen, false);
        if (!state.ContainsKey(StateKeys.Preferences.Fullscreen))
            state.Set(StateKeys.Preferences.Fullscreen, false);

        // 屏幕震动
        if (!state.ContainsKey(StateKeys.Shake.Active))
            state.Set(StateKeys.Shake.Active, false);

        // CG鉴赏
        if (!state.ContainsKey(StateKeys.Gallery.Unlocked))
            state.Set(StateKeys.Gallery.Unlocked, new List<GalleryEntry>());
        if (!state.ContainsKey(StateKeys.Gallery.Visible))
            state.Set(StateKeys.Gallery.Visible, false);

        // 调试控制台
        if (!state.ContainsKey(StateKeys.Debug.Logs))
            state.Set(StateKeys.Debug.Logs, new List<DebugLogEntry>());
        if (!state.ContainsKey(StateKeys.Debug.Visible))
            state.Set(StateKeys.Debug.Visible, false);
        if (!state.ContainsKey(StateKeys.Debug.Enabled))
            state.Set(StateKeys.Debug.Enabled, false);
        if (!state.ContainsKey(StateKeys.Debug.MaxLogs))
            state.Set(StateKeys.Debug.MaxLogs, 500);

        // NVL 模式
        if (!state.ContainsKey(StateKeys.Nvl.Active))
            state.Set(StateKeys.Nvl.Active, false);
        if (!state.ContainsKey(StateKeys.Nvl.Text))
            state.Set(StateKeys.Nvl.Text, "");
        if (!state.ContainsKey(StateKeys.Nvl.Speakers))
            state.Set(StateKeys.Nvl.Speakers, "");
        if (!state.ContainsKey(StateKeys.Nvl.Count))
            state.Set(StateKeys.Nvl.Count, 0);

        // 打字机完成标记（供 Skip/Auto 模式检测）
        if (!state.ContainsKey(StateKeys.Dialog.TypewriterDone))
            state.Set(StateKeys.Dialog.TypewriterDone, true);
    }
}
