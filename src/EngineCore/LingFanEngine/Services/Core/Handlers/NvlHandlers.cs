using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// NVL 模式命令处理器
/// <para>nvl：进入 NVL 模式，后续对话累积显示在同一个文本框中。</para>
/// <para>nvl clear：清空 NVL 累积文本并退出 NVL 模式。</para>
/// <para>对标 Ren'Py NVL Mode 和 KiriKiri 的历史对话模式。</para>
/// </summary>
public class NvlHandler : ICommandHandler<NvlCommand>
{
    public void Handle(NvlCommand cmd, ICommandContext ctx)
    {
        if (cmd.IsClear)
        {
            // 清空 NVL 累积文本并退出 NVL 模式
            ctx.State.Set(StateKeys.Nvl.Active, false);
            ctx.State.Set(StateKeys.Nvl.Text, "");
            ctx.State.Set(StateKeys.Nvl.Speakers, "");
            ctx.State.Set(StateKeys.Nvl.Count, 0);
        }
        else
        {
            // 进入 NVL 模式
            ctx.State.Set(StateKeys.Nvl.Active, true);
            // 不清空文本，允许连续 nvl 语句累积
            if (!ctx.State.ContainsKey(StateKeys.Nvl.Text))
                ctx.State.Set(StateKeys.Nvl.Text, "");
            if (!ctx.State.ContainsKey(StateKeys.Nvl.Speakers))
                ctx.State.Set(StateKeys.Nvl.Speakers, "");
            if (!ctx.State.ContainsKey(StateKeys.Nvl.Count))
                ctx.State.Set(StateKeys.Nvl.Count, 0);
        }
    }
}
