using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// NVL 模式命令处理器
/// <para>nvl：进入 NVL 模式，后续对话累积显示在同一个文本框中。</para>
/// <para>nvl clear：清空 NVL 累积文本（不退出 NVL 模式——对标 Ren'Py nvl clear）。</para>
/// <para>nvl exit：退出 NVL 模式并清空累积文本（恢复 ADV 模式）。</para>
/// <para>对标 Ren'Py NVL Mode 和 KiriKiri 的历史对话模式。</para>
/// </summary>
public class NvlHandler : ICommandHandler<NvlCommand>, IDefaultCommandHandler
{
    public void Handle(NvlCommand cmd, ICommandContext ctx)
    {
        if (cmd.IsExit)
        {
            // 退出 NVL 模式——清空累积文本并关闭 NVL 激活状态
            ctx.State.Set(StateKeys.Nvl.Active, false);
            ctx.State.Set(StateKeys.Nvl.Text, "");
            ctx.State.Set(StateKeys.Nvl.Speakers, "");
            ctx.State.Set(StateKeys.Nvl.Count, 0);
            // 同步清空对话框显示
            ctx.State.Set(StateKeys.Dialog.Text, "");
            ctx.State.Set(StateKeys.Dialog.Speaker, "");
            ctx.State.Set(StateKeys.Dialog.Complete, false);

            // 「nvl auto」作用域收尾：若由 nvl auto 开启的自动推进，退出 NVL 时一并关闭，
            // 避免污染后续 ADV 对话（作用域语义：进 NVL 自动开、出 nvl exit 自动停）。
            if (ctx.State.Get<bool>(StateKeys.Nvl.AutoScoped))
            {
                ctx.State.Set(StateKeys.Playback.AutoActive, false);
                ctx.State.Set(StateKeys.Playback.AutoTimer, 0.0);
                ctx.State.Set(StateKeys.Nvl.AutoScoped, false);
            }
        }
        else if (cmd.IsClear)
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
            // 注意：nvl clear 不触碰 AutoScoped/AutoActive——作用域自动推进在 clear 后继续有效
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

            // 「nvl auto」= 进入 NVL 并开启作用域自动推进：
            // 1) 打开自动模式（与 skip 互斥，转自动时关 skip）
            // 2) 标记 AutoScoped，使 nvl exit 能自动收尾
            // 3) 退出回溯浏览态（开启自动意味着用户要继续前进）
            if (cmd.IsAuto)
            {
                ctx.State.Set(StateKeys.Playback.AutoActive, true);
                ctx.State.Set(StateKeys.Playback.AutoTimer, 0.0);
                ctx.State.Set(StateKeys.Playback.SkipActive, false);
                ctx.State.Set(StateKeys.Nvl.AutoScoped, true);
                ctx.State.Set(StateKeys.Rollback.IsActive, false);
                ctx.State.Set(StateKeys.Rollback.IsReplay, false);
            }
        }
    }
}
