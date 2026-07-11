using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 场景级后退命令处理器（DSL `back` 关键字）
/// <para>纯场景导航——在 SceneStack 中后退到上一个场景。</para>
/// <para>与 RollbackHandler 不同——本命令不回退对话，而是跳转场景。</para>
/// <para>场景级导航清空 DSL 检查点（硬跳转语义）。</para>
/// </summary>
public class BackHandler : ICommandHandler<BackCommand>, IDefaultCommandHandler
{
    public void Handle(BackCommand bc, ICommandContext ctx)
    {
        if (ctx.SceneStack == null || ctx.SceneStack.Count == 0)
            return;

        ctx.ResetInteractionState();
        ctx.ClearLocalVariables();
        var backSnapshot = ctx.SceneStack.Back();
        if (backSnapshot != null)
        {
            var sceneName = backSnapshot.SceneName;
            ctx.State.Set(StateKeys.Scene.CurrentName, sceneName);
            ctx.State.Set(StateKeys.Screen.ActiveScreen, sceneName);
            ctx.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
            ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
            ctx.State.Set(StateKeys.Scene.Dirty, true);
            SceneStackDslRestarter.Restart(sceneName, ctx);
        }
    }
}

/// <summary>
/// 场景级前进命令处理器（DSL `forward` 关键字）
/// <para>纯场景导航——在 SceneStack 中前进到之前后退过的场景。</para>
/// <para>与 RollforwardHandler 不同——本命令不前进对话，而是跳转场景。</para>
/// <para>场景级导航清空 DSL 检查点（硬跳转语义）。</para>
/// </summary>
public class ForwardHandler : ICommandHandler<ForwardCommand>, IDefaultCommandHandler
{
    public void Handle(ForwardCommand fc, ICommandContext ctx)
    {
        if (ctx.SceneStack == null || ctx.SceneStack.ForwardCount == 0)
            return;

        ctx.ResetInteractionState();
        ctx.ClearLocalVariables();
        var fwdSnapshot = ctx.SceneStack.Forward();
        if (fwdSnapshot != null)
        {
            var sceneName = fwdSnapshot.SceneName;
            ctx.State.Set(StateKeys.Scene.CurrentName, sceneName);
            ctx.State.Set(StateKeys.Screen.ActiveScreen, sceneName);
            ctx.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
            ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
            ctx.State.Set(StateKeys.Scene.Dirty, true);
            SceneStackDslRestarter.Restart(sceneName, ctx);
        }
    }
}

/// <summary>清空场景堆栈命令处理器</summary>
public class ClearStackHandler : ICommandHandler<ClearStackCommand>, IDefaultCommandHandler
{
    public void Handle(ClearStackCommand command, ICommandContext ctx)
        => ctx.SceneStack?.Clear();
}
