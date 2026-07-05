using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// CG 解锁命令处理器
/// <para>将 CG 条目写入状态容器的已解锁列表，供鉴赏界面读取。</para>
/// </summary>
public class UnlockGalleryHandler : ICommandHandler<UnlockGalleryCommand>
{
    public void Handle(UnlockGalleryCommand cmd, ICommandContext ctx)
    {
        var list = ctx.State.Get<List<GalleryEntry>>(StateKeys.Gallery.Unlocked) ?? [];

        // 已解锁则跳过（幂等）
        if (list.Any(e => e.Id == cmd.Id))
            return;

        list.Add(new GalleryEntry
        {
            Id = cmd.Id,
            ImagePath = cmd.ImagePath,
            Title = cmd.Title,
            SceneName = cmd.SceneName
        });

        ctx.State.Set(StateKeys.Gallery.Unlocked, list);
    }
}
