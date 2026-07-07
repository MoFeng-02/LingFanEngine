using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 屏幕震动命令处理器
/// <para>设置震动状态到状态容器，GameLoop 每帧推进震动计时并计算偏移。</para>
/// <para>SceneView 读取 __shake_offset_x/y 应用 TranslateTransform。</para>
/// </summary>
public class ShakeHandler : ICommandHandler<ShakeCommand>, IDefaultCommandHandler
{
    public void Handle(ShakeCommand sc, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.Shake.Active, true);
        ctx.State.Set(StateKeys.Shake.Intensity, sc.Intensity);
        ctx.State.Set(StateKeys.Shake.Duration, sc.Duration);
        ctx.State.Set(StateKeys.Shake.Elapsed, 0.0);
        ctx.State.Set(StateKeys.Shake.OffsetX, 0.0);
        ctx.State.Set(StateKeys.Shake.OffsetY, 0.0);
    }
}

/// <summary>
/// 跳过模式切换命令处理器
/// <para>开启时自动跳过对话等待（每帧检测后立即设置 dialog_complete）。</para>
/// <para>关闭时恢复正常对话等待。</para>
/// </summary>
public class ToggleSkipHandler : ICommandHandler<ToggleSkipCommand>, IDefaultCommandHandler
{
    public void Handle(ToggleSkipCommand _, ICommandContext ctx)
    {
        var current = ctx.State.Get<bool>(StateKeys.Playback.SkipActive);
        var newState = !current;
        ctx.State.Set(StateKeys.Playback.SkipActive, newState);

        // 开启跳过模式时关闭自动模式（互斥）
        if (newState)
        {
            ctx.State.Set(StateKeys.Playback.AutoActive, false);
            ctx.State.Set(StateKeys.Playback.AutoTimer, 0.0);
        }
    }
}

/// <summary>
/// 自动模式切换命令处理器
/// <para>开启时对话完成后自动等待一段时间再推进。</para>
/// <para>关闭时恢复正常对话等待。</para>
/// </summary>
public class ToggleAutoHandler : ICommandHandler<ToggleAutoCommand>, IDefaultCommandHandler
{
    public void Handle(ToggleAutoCommand _, ICommandContext ctx)
    {
        var current = ctx.State.Get<bool>(StateKeys.Playback.AutoActive);
        var newState = !current;
        ctx.State.Set(StateKeys.Playback.AutoActive, newState);
        ctx.State.Set(StateKeys.Playback.AutoTimer, 0.0);

        // 开启自动模式时关闭跳过模式（互斥）
        if (newState)
        {
            ctx.State.Set(StateKeys.Playback.SkipActive, false);
        }
    }
}
