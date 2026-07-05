using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 播放 BGM 命令处理器
/// </summary>
public class PlayBgmHandler : ICommandHandler<PlayBgmCommand>
{
    public void Handle(PlayBgmCommand bgm, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.Audio.CurrentBgmPath, bgm.Path);
        ctx.State.Set(StateKeys.Audio.CurrentBgmVolume, bgm.Volume);
        ctx.State.Set(StateKeys.Audio.BgmAutoStop, bgm.AutoStop);
        ctx.AudioManager?.PlayBgm(bgm.Path, bgm.Volume);
    }
}

/// <summary>
/// 播放音效命令处理器
/// </summary>
public class PlaySeHandler : ICommandHandler<PlaySeCommand>
{
    public void Handle(PlaySeCommand se, ICommandContext ctx)
    {
        ctx.AudioManager?.PlaySe(se.Path, se.Volume);
    }
}

/// <summary>
/// 播放语音命令处理器
/// </summary>
public class PlayVoiceHandler : ICommandHandler<PlayVoiceCommand>
{
    public void Handle(PlayVoiceCommand vc, ICommandContext ctx)
    {
        ctx.AudioManager?.PlayVoice(vc.Path, vc.Volume);
        ctx.State.Set(StateKeys.Audio.VoiceAutoStop, vc.AutoStop);
    }
}

/// <summary>
/// BGM 交叉淡入队列命令处理器
/// </summary>
public class BgmQueueHandler : ICommandHandler<BgmQueueCommand>
{
    public void Handle(BgmQueueCommand q, ICommandContext ctx)
    {
        _ = ctx.AudioManager?.QueueBgmAsync(q.Path, q.Volume, q.CrossFadeDuration);
    }
}
