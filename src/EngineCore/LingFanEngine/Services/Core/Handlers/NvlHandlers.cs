using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// NVL 模式命令处理器
/// <para>nvl：进入 NVL 模式，后续对话累积显示在同一个文本框中。</para>
/// <para>nvl clear：清空 NVL 累积文本（保持 NVL 模式激活——对标 Ren'Py nvl clear）。</para>
/// <para>对标 Ren'Py NVL Mode 和 KiriKiri 的历史对话模式。</para>
/// </summary>
public class NvlHandler : ICommandHandler<NvlCommand>, IDefaultCommandHandler
{
    public void Handle(NvlCommand cmd, ICommandContext ctx)
    {
        if (cmd.IsClear)
        {
            // 清空 NVL 累积文本——但不退出 NVL 模式（对标 Ren'Py nvl clear）
            // Ren'Py 中 nvl clear 只清空文本，NVL 角色后续对话继续累积
            ctx.State.Set(StateKeys.Nvl.Text, "");
            ctx.State.Set(StateKeys.Nvl.Speakers, "");
            ctx.State.Set(StateKeys.Nvl.Count, 0);
            // 同步清空对话框显示
            ctx.State.Set(StateKeys.Dialog.Text, "");
            ctx.State.Set(StateKeys.Dialog.Speaker, "");
            ctx.State.Set(StateKeys.Dialog.Complete, false);
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
