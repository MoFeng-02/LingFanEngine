using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 播放视频命令处理器
/// <para>通过 VideoManager 写入状态键，驱动 SceneView 中的 GpuMediaPlayer 控件。</para>
/// </summary>
public class PlayVideoHandler : ICommandHandler<PlayVideoCommand>, IDefaultCommandHandler
{
    public void Handle(PlayVideoCommand cmd, ICommandContext ctx)
    {
        ctx.VideoManager?.Play(cmd.Path, cmd.Volume, cmd.Loop, cmd.AutoPlay);
    }
}

/// <summary>
/// 停止视频命令处理器
/// </summary>
public class StopVideoHandler : ICommandHandler<StopVideoCommand>, IDefaultCommandHandler
{
    public void Handle(StopVideoCommand cmd, ICommandContext ctx)
    {
        ctx.VideoManager?.Stop();
    }
}

/// <summary>
/// 暂停视频命令处理器
/// </summary>
public class PauseVideoHandler : ICommandHandler<PauseVideoCommand>, IDefaultCommandHandler
{
    public void Handle(PauseVideoCommand cmd, ICommandContext ctx)
    {
        ctx.VideoManager?.Pause();
    }
}

/// <summary>
/// 恢复视频播放命令处理器
/// </summary>
public class ResumeVideoHandler : ICommandHandler<ResumeVideoCommand>, IDefaultCommandHandler
{
    public void Handle(ResumeVideoCommand cmd, ICommandContext ctx)
    {
        ctx.VideoManager?.Resume();
    }
}

/// <summary>
/// 视频跳转命令处理器
/// </summary>
public class SeekVideoHandler : ICommandHandler<SeekVideoCommand>, IDefaultCommandHandler
{
    public void Handle(SeekVideoCommand cmd, ICommandContext ctx)
    {
        ctx.VideoManager?.Seek(TimeSpan.FromSeconds(cmd.Position));
    }
}

/// <summary>
/// 全屏过场动画命令处理器
/// <para>通过 VideoManager.PlayCutscene 设置过场模式状态键。</para>
/// <para>阻塞等待由 GameController.PlayCutsceneAsync 实现。</para>
/// </summary>
public class CutsceneHandler : ICommandHandler<CutsceneCommand>, IDefaultCommandHandler
{
    public void Handle(CutsceneCommand cmd, ICommandContext ctx)
    {
        ctx.VideoManager?.PlayCutscene(cmd.Path, cmd.Skipable, cmd.Volume);
    }
}
