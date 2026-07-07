using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 输入命令处理器 — 将输入请求写入状态容器供渲染层展示
/// </summary>
public class InputHandler : ICommandHandler<InputCommand>, IDefaultCommandHandler
{
    public void Handle(InputCommand ic, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.Input.DslPrompt, ic.Prompt);
        ctx.State.Set(StateKeys.Input.DslStore, ic.StoreKey);
        ctx.State.Set(StateKeys.Input.DslOptions, ic.Options != null ? string.Join(",", ic.Options) : "");
        ctx.State.Set(StateKeys.Input.DslWaiting, true);
    }
}

/// <summary>
/// 等待命令处理器 — 设置等待标记和时长
/// </summary>
public class WaitHandler : ICommandHandler<WaitCommand>, IDefaultCommandHandler
{
    public void Handle(WaitCommand wc, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.Dsl.Waiting, true);
        ctx.State.Set(StateKeys.Dsl.WaitDuration, wc.Seconds);
    }
}

/// <summary>
/// 硬暂停命令处理器 — 等待用户点击（对标 Ren'Py pause hard）
/// </summary>
public class HardPauseHandler : ICommandHandler<HardPauseCommand>, IDefaultCommandHandler
{
    public void Handle(HardPauseCommand command, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.Dialog.WaitingSayComplete, false);
    }
}
