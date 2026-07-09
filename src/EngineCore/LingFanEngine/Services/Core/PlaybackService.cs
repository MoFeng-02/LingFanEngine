using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 播放控制服务实现
/// <para>处理 Skip/Auto 模式的每帧逻辑——DSL 执行器异步运行时调用。</para>
/// <para>Skip：对话等待中时立即设置 dialog_complete=true</para>
/// <para>Auto：对话等待中时累计计时器，达到延迟后设置 dialog_complete=true</para>
/// </summary>
public class PlaybackService : IPlaybackService
{
    /// <inheritdoc/>
    public void Process(double frameDelta, IStateContainer state)
    {
        // Menu/UI 场景不执行 Skip/Auto（菜单没有 Say，不应自动推进）
        var currentType = (SceneType)state.Get<int>(StateKeys.Scene.CurrentType);
        if (currentType != SceneType.Game) return;

        var waitingType = state.Get<string>(StateKeys.Dsl.WaitingType);
        // P1-#12: Skip/Auto 扩展到 WaitSkipable 和 Pause（不仅限于 Dialog）
        var isInteractive = waitingType == StateKeys.Dsl.WaitingTypes.Dialog
            || waitingType == StateKeys.Dsl.WaitingTypes.WaitSkipable
            || waitingType == StateKeys.Dsl.WaitingTypes.Pause;
        if (!isInteractive) return;

        // 回溯模式下不自动推进（用户在浏览历史，不应被跳过/自动模式打断）
        if (state.Get<bool>(StateKeys.Rollback.IsActive)) return;

        // 对话已完成（用户点击或打字机结束）时不处理
        if (state.Get<bool>(StateKeys.Dialog.Complete)) return;

        // P1-#12: 打字机检查仅对 Dialog 等待有意义
        // WaitSkipable/Pause 没有打字机，直接推进
        if (waitingType == StateKeys.Dsl.WaitingTypes.Dialog)
        {
            var typewriterDone = state.Get<bool>(StateKeys.Dialog.TypewriterDone);
            if (!typewriterDone) return;
        }

        // 跳过模式
        if (state.Get<bool>(StateKeys.Playback.SkipActive))
        {
            // P1-#12: SkipOnlySeen 检查仅对 Dialog 有意义
            if (waitingType == StateKeys.Dsl.WaitingTypes.Dialog)
            {
                var skipOnlySeen = state.Get<bool>(StateKeys.Playback.SkipOnlySeen);
                if (skipOnlySeen)
                {
                    var currentIndex = state.Get<int>(StateKeys.Dsl.CurrentIndex);
                    var sceneName = state.Get<string>(StateKeys.Scene.CurrentName) ?? "";
                    var seenKey = $"{sceneName}:{currentIndex}";
                    var seen = state.Get<HashSet<string>>(StateKeys.Playback.SeenSayIndices);
                    if (seen != null && !seen.Contains(seenKey))
                    {
                        // 未读 Say — 停止跳过
                        state.Set(StateKeys.Playback.SkipActive, false);
                        return;
                    }
                }
            }
            state.Set(StateKeys.Dialog.Complete, true);
            return;
        }

        // 自动模式：累计计时器，达到延迟后推进
        if (state.Get<bool>(StateKeys.Playback.AutoActive))
        {
            var timer = state.Get<double>(StateKeys.Playback.AutoTimer) + frameDelta;
            var delay = state.Get<double>(StateKeys.Playback.AutoDelay);
            if (delay <= 0) delay = 3.0; // 默认 3 秒

            if (timer >= delay)
            {
                state.Set(StateKeys.Dialog.Complete, true);
                state.Set(StateKeys.Playback.AutoTimer, 0.0);
            }
            else
            {
                state.Set(StateKeys.Playback.AutoTimer, timer);
            }
        }
    }
}
