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
        if (waitingType != StateKeys.Dsl.WaitingTypes.Dialog) return;

        // 回溯模式下不自动推进（用户在浏览历史，不应被跳过/自动模式打断）
        if (state.Get<bool>(StateKeys.Rollback.IsActive)) return;

        // 对话已完成（用户点击或打字机结束）时不处理
        if (state.Get<bool>(StateKeys.Dialog.Complete)) return;

        // 检查打字机是否还在进行中（由 SceneView 控制 __typewriter_done）
        // 如果打字机未完成，跳过模式和自动模式都应等待
        var typewriterDone = state.Get<bool>(StateKeys.Dialog.TypewriterDone);
        if (!typewriterDone) return;

        // 跳过模式
        if (state.Get<bool>(StateKeys.Playback.SkipActive))
        {
            // 检查当前 Say 是否已读（SkipOnlySeen=true 时仅跳已读）
            var skipOnlySeen = state.Get<bool>(StateKeys.Playback.SkipOnlySeen);
            if (skipOnlySeen)
            {
                var currentIndex = state.Get<int>(StateKeys.Dsl.CurrentIndex);
                var sceneName = state.Get<string>(StateKeys.Scene.CurrentName) ?? "";
                var seenKey = $"{sceneName}:{currentIndex}";
                var seen = state.Get<HashSet<string>>(StateKeys.Playback.SeenSayIndices);
                // currentIndex 指向当前正在等待的 ShowDialogCommand（不再提前推进）
                if (seen != null && !seen.Contains(seenKey))
                {
                    // 未读 Say — 停止跳过
                    state.Set(StateKeys.Playback.SkipActive, false);
                    return;
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
