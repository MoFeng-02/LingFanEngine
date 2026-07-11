using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 状态初始化器实�?
/// <para>在引擎启动时初始化所有默认状态键值（仅当键不存在时写入）�?/para>
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
if (!state.ContainsKey(StateKeys.Playback.AutoActive))
            state.Set(StateKeys.Playback.AutoActive, false);
        if (!state.ContainsKey(StateKeys.Playback.AutoDelay))
            state.Set(StateKeys.Playback.AutoDelay, 3.0);
        if (!state.ContainsKey(StateKeys.Playback.AutoTimer))
            state.Set(StateKeys.Playback.AutoTimer, 0.0);

        // 场景类型（默�?Game�?
        if (!state.ContainsKey(StateKeys.Scene.CurrentType))
            state.Set(StateKeys.Scene.CurrentType, (int)Abstractions.Entities.Enums.SceneType.Game);

        // 偏好设置默认�?
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

        // 调试控制�?
        if (!state.ContainsKey(StateKeys.Debug.Logs))
            state.Set(StateKeys.Debug.Logs, new List<DebugLogEntry>());
        if (!state.ContainsKey(StateKeys.Debug.Visible))
            state.Set(StateKeys.Debug.Visible, false);
        if (!state.ContainsKey(StateKeys.Debug.Enabled))
            state.Set(StateKeys.Debug.Enabled, false);
        if (!state.ContainsKey(StateKeys.Debug.MaxLogs))
            state.Set(StateKeys.Debug.MaxLogs, 500);

        // 性能 HUD
        if (!state.ContainsKey(StateKeys.Performance.ShowHud))
            state.Set(StateKeys.Performance.ShowHud, false);

        // NVL 模式
        if (!state.ContainsKey(StateKeys.Nvl.Active))
            state.Set(StateKeys.Nvl.Active, false);
        if (!state.ContainsKey(StateKeys.Nvl.Text))
            state.Set(StateKeys.Nvl.Text, "");
        if (!state.ContainsKey(StateKeys.Nvl.Speakers))
            state.Set(StateKeys.Nvl.Speakers, "");
        if (!state.ContainsKey(StateKeys.Nvl.Count))
            state.Set(StateKeys.Nvl.Count, 0);

        // 打字机完成标记（�?Skip/Auto 模式检测）
        if (!state.ContainsKey(StateKeys.Dialog.TypewriterDone))
            state.Set(StateKeys.Dialog.TypewriterDone, true);

        // 视频默认状�?
        if (!state.ContainsKey(StateKeys.Video.CurrentPath))
            state.Set(StateKeys.Video.CurrentPath, "");
        if (!state.ContainsKey(StateKeys.Video.IsPlaying))
            state.Set(StateKeys.Video.IsPlaying, false);
        if (!state.ContainsKey(StateKeys.Video.IsPaused))
            state.Set(StateKeys.Video.IsPaused, false);
        if (!state.ContainsKey(StateKeys.Video.Volume))
            state.Set(StateKeys.Video.Volume, 1.0f);
        if (!state.ContainsKey(StateKeys.Video.Loop))
            state.Set(StateKeys.Video.Loop, false);
        if (!state.ContainsKey(StateKeys.Video.AutoPlay))
            state.Set(StateKeys.Video.AutoPlay, true);
        if (!state.ContainsKey(StateKeys.Video.SeekPosition))
            state.Set<object?>(StateKeys.Video.SeekPosition, null);
        if (!state.ContainsKey(StateKeys.Video.Position))
            state.Set(StateKeys.Video.Position, 0.0);
        if (!state.ContainsKey(StateKeys.Video.Duration))
            state.Set(StateKeys.Video.Duration, 0.0);
        if (!state.ContainsKey(StateKeys.Video.IsFinished))
            state.Set(StateKeys.Video.IsFinished, false);
        if (!state.ContainsKey(StateKeys.Video.CutsceneActive))
            state.Set(StateKeys.Video.CutsceneActive, false);
        if (!state.ContainsKey(StateKeys.Video.CutsceneSkipped))
            state.Set(StateKeys.Video.CutsceneSkipped, false);
        if (!state.ContainsKey(StateKeys.Video.CutsceneSkipable))
            state.Set(StateKeys.Video.CutsceneSkipable, true);

        // 对话基础状态键默认值（显式初始化，不依赖 default(T) 隐式行为）
        if (!state.ContainsKey(StateKeys.Dialog.Text))
            state.Set(StateKeys.Dialog.Text, "");
        if (!state.ContainsKey(StateKeys.Dialog.Speaker))
            state.Set(StateKeys.Dialog.Speaker, "");
        if (!state.ContainsKey(StateKeys.Dialog.Complete))
            state.Set(StateKeys.Dialog.Complete, false);
        if (!state.ContainsKey(StateKeys.Dialog.Clickable))
            state.Set(StateKeys.Dialog.Clickable, false);
        // Phase 37: noskip 标记
        if (!state.ContainsKey(StateKeys.Dialog.Noskip))
            state.Set(StateKeys.Dialog.Noskip, false);
        if (!state.ContainsKey(StateKeys.Dialog.TypewriterEnabled))
            state.Set(StateKeys.Dialog.TypewriterEnabled, true);

        // Phase 24: 对话窗口模式与侧脸图
        if (!state.ContainsKey(StateKeys.Dialog.WindowMode))
            state.Set(StateKeys.Dialog.WindowMode, "auto");
        if (!state.ContainsKey(StateKeys.Dialog.SideImage))
            state.Set<object?>(StateKeys.Dialog.SideImage, null);

// Phase 24: 回溯阻止标记
if (!state.ContainsKey(StateKeys.Rollback.BlockedUntil))
state.Set(StateKeys.Rollback.BlockedUntil, -1);

// C# 场景回放代次（回溯取消机制）
if (!state.ContainsKey(StateKeys.Dsl.CSharpReplayGeneration))
state.Set(StateKeys.Dsl.CSharpReplayGeneration, 0);

        // Phase 24: Screen 状�?
        if (!state.ContainsKey(StateKeys.Screen.Params))
            state.Set<object?>(StateKeys.Screen.Params, null);
        if (!state.ContainsKey(StateKeys.Screen.ActiveScreen))
            state.Set(StateKeys.Screen.ActiveScreen, "");
    }
}
