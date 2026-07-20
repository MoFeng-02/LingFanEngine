using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 播放控制服务实现
/// <para>处理 Skip/Auto 模式的每帧逻辑——DSL 执行器异步运行时调用。</para>
/// <para>Skip：对话等待中时立即设置 dialog_complete=true</para>
/// <para>Auto：对话等待中时累计计时器，达到延迟后设置 dialog_complete=true</para>
/// <para>Phase 37: 对齐 Ren'Py Skip 行为——菜单/输入停止 Skip、打字机瞬时、过渡加速、noskip 标记</para>
/// </summary>
public class PlaybackService : IPlaybackService
{
    /// <summary>
    /// Skip 模式最小推进间隔（秒）——防止跳过太快，让文字可辨识。
    /// 0.05s ≈ 20 条/秒（对标 Ren'Py Skip 速度）。
    /// </summary>
    private const double SkipIntervalSeconds = 0.05;

    /// <summary>Skip 推进计时器——累积 frameDelta，达到 SkipIntervalSeconds 后推进一次</summary>
    private double _skipTimer;

    /// <inheritdoc/>
    public void Process(double frameDelta, IStateContainer state)
    {
        // Menu/UI 场景不执行 Skip/Auto（菜单没有 SayAsync，不应自动推进）
        var currentType = (SceneType)state.Get<int>(StateKeys.Scene.CurrentType);
        if (currentType != SceneType.Game) return;

        // Phase 37: Skip 加速过渡——过渡进行中时立即完成
        if (state.Get<bool>(StateKeys.Playback.SkipActive)
            && state.Get<bool>(StateKeys.Transition.Active))
        {
            state.Set(StateKeys.Transition.Active, false);
            state.Set(StateKeys.Transition.Progress, 1.0);
            state.Set(StateKeys.Transition.OffsetX, 0.0);
            state.Set(StateKeys.Transition.OffsetY, 0.0);
            state.Set(StateKeys.Transition.Scale, 1.0);
            return;
        }

        var waitingType = state.Get<string>(StateKeys.Dsl.WaitingType);

        // Phase 37: Skip 遇到菜单/输入时自动关闭（对标 Ren'Py）
        if (waitingType == StateKeys.Dsl.WaitingTypes.Menu
            || waitingType == StateKeys.Dsl.WaitingTypes.Input)
        {
            if (state.Get<bool>(StateKeys.Playback.SkipActive))
            {
                state.Set(StateKeys.Playback.SkipActive, false);
                _skipTimer = 0; // 停止时重置计时器
            }
            return;
        }

        // P1-#12: Skip/Auto 扩展到 WaitSkipableAsync 和 Pause（不仅限于 Dialog）
        var isInteractive = waitingType == StateKeys.Dsl.WaitingTypes.Dialog
            || waitingType == StateKeys.Dsl.WaitingTypes.WaitSkipable
            || waitingType == StateKeys.Dsl.WaitingTypes.Pause;
        if (!isInteractive) return;

        // 回溯模式下不自动推进（用户在浏览历史，不应被跳过/自动模式打断）
        if (state.Get<bool>(StateKeys.Rollback.IsActive)) return;

        // 对话已完成（用户点击或打字机结束）时不处理
        if (state.Get<bool>(StateKeys.Dialog.Complete)) return;

        // 跳过模式
        if (state.Get<bool>(StateKeys.Playback.SkipActive))
        {
            // Phase 37: noskip 标记——此对话不可跳过，玩家必须手动点击
            // Skip 保持激活，下一条对话恢复正常跳过
            if (waitingType == StateKeys.Dsl.WaitingTypes.Dialog
                && state.Get<bool>(StateKeys.Dialog.Noskip))
            {
                _skipTimer = 0; // noskip 不推进，重置计时器
                return;
            }

            // Phase 37: 仅跳已读检查——直接用 Preferences.SkipUnseen（单一真相源）
            // SkipUnseen=false（默认）→ 仅跳已读；SkipUnseen=true → 跳所有
            if (waitingType == StateKeys.Dsl.WaitingTypes.Dialog)
            {
                var skipUnseen = state.Get<bool>(StateKeys.Preferences.SkipUnseen);
                if (!skipUnseen)
                {
                    var currentIndex = state.Get<int>(StateKeys.Dsl.CurrentIndex);
                    var sceneName = state.Get<string>(StateKeys.Scene.CurrentName) ?? "";
                    var seenKey = $"{sceneName}:{currentIndex}";
                    var seen = state.Get<HashSet<string>>(StateKeys.Playback.SeenSayIndices);
                    if (seen != null && !seen.Contains(seenKey))
                    {
                        // 未读 SayAsync — 停止跳过
                        state.Set(StateKeys.Playback.SkipActive, false);
                        _skipTimer = 0; // 停止时重置计时器
                        return;
                    }
                }
            }

            // Phase 41: 最小推进间隔——防止跳过太快，让文字可辨识
            _skipTimer += frameDelta;
            if (_skipTimer < SkipIntervalSeconds)
                return;
            _skipTimer = 0;

            // Phase 37: Skip 模式下打字机瞬时完成（对标 Ren'Py）
            if (waitingType == StateKeys.Dsl.WaitingTypes.Dialog)
                state.Set(StateKeys.Dialog.TypewriterDone, true);

            state.Set(StateKeys.Dialog.Complete, true);
            return;
        }

        // 自动模式：累计计时器，达到延迟后推进
        if (state.Get<bool>(StateKeys.Playback.AutoActive))
        {
            // P1-#12: 打字机检查仅对 Dialog 有意义
            // WaitSkipableAsync/Pause 没有打字机，直接推进
            if (waitingType == StateKeys.Dsl.WaitingTypes.Dialog)
            {
                var typewriterDone = state.Get<bool>(StateKeys.Dialog.TypewriterDone);
                if (!typewriterDone) return;
            }

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
