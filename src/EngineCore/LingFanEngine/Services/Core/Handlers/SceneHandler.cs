using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>场景命令处理器 — 清栈 + 切场景</summary>
public class SceneHandler : ICommandHandler<SceneCommand>, IDefaultCommandHandler
{
    public void Handle(SceneCommand sc, ICommandContext ctx)
    {
        var sceneEntity = ctx.SceneRegistry?.FindScene(sc.SceneName);
        var sceneType = sceneEntity?.SceneType ?? SceneType.Game;

        if (sceneType == SceneType.Game)
        {
            ctx.ClearLocalVariables();
            ctx.DslExecutor?.ClearCheckpoints();  // Scene 命令是硬重置，清检查点
            ctx.ResetInteractionState();
            ctx.SceneStack?.Clear();
            // 同为硬重置：清除 Menu 返回标记，防止后续 navigate 误判为"返回游戏"
            ctx.State.Set(StateKeys.Scene.MenuReturnTo, (string?)null);
        }
        else
        {
            ctx.ResetInteractionState();
            // scene 命令是硬重置——清除 Menu 返回状态，防止后续 navigate 误判为"返回游戏"
            ctx.State.Set(StateKeys.Scene.MenuReturnTo, (string?)null);
            ctx.State.Set(StateKeys.Scene.GameDslIndex, 0);
            ctx.State.Set(StateKeys.Scene.GameDslWaitingType, "");
            ctx.State.Set<object?>(StateKeys.Scene.GameSceneElements, null);
            ctx.State.Set<object?>(StateKeys.Scene.GameRuntimeElements, null);
            ctx.State.Set(StateKeys.Scene.GameCurrentBackground, "");
        }

        ctx.State.Set(StateKeys.Scene.CurrentType, (int)sceneType);
        System.Diagnostics.Debug.WriteLine($"[SceneHandler] SceneName={sc.SceneName} type={sceneType}");

        if (sceneEntity != null)
        {
            // 深合并场景级 Defines
            if (sceneEntity.Defines != null && sceneEntity.Defines.Count > 0)
                GameLoop.MergeIntoState(sceneEntity.Defines, ctx.State);

            ctx.State.Set(StateKeys.Scene.CurrentName, sc.SceneName);
            ctx.State.Set(StateKeys.Screen.ActiveScreen, sc.SceneName);
            // 元素列表初始化为空——UI 元素现在由 DslExecutor 通过 ShowElementCommand 按序追加
            // 不设 Dirty——场景名变更已触发 SceneView.RebuildScene
            ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
        }
        else
        {
            ctx.State.Set(StateKeys.Scene.CurrentName, sc.SceneName);
            ctx.State.Set(StateKeys.Screen.ActiveScreen, sc.SceneName);
            ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
        }

        // 启动 DSL 执行器（尝试场景同名 label，与 NavigateHandler 一致）
        if (ctx.DslExecutor != null && ctx.StoryRegistry != null && sceneEntity != null)
        {
            var (cmds, lbls) = ctx.StoryRegistry.GetCompiledResult(sc.SceneName);
            if (cmds != null && lbls != null && lbls.ContainsKey(sc.SceneName))
            {
                ctx.DslExecutor.LoadCommands(cmds, lbls);
                ctx.DslExecutor.StartFromLabel(sc.SceneName);
                System.Diagnostics.Debug.WriteLine($"[SceneHandler] 启动场景同名 label: {sc.SceneName}");
            }
        }
    }
}
