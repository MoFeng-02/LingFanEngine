using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>导航到 DSL label 命令处理器</summary>
public class NavToLabelHandler : ICommandHandler<NavToLabelCommand>, IDefaultCommandHandler
{
    public void Handle(NavToLabelCommand ntl, ICommandContext ctx)
    {
        System.Diagnostics.Debug.WriteLine($"[NavToLabelHandler] TargetLabel={ntl.TargetLabel}");
        ctx.DslExecutor?.StartFromLabel(ntl.TargetLabel);
    }
}

/// <summary>
/// 重置全部游戏状态命令处理器（返回主菜单时手动调用）
/// <para>清除所有非系统变量、局部变量、场景堆栈、回溯检查点、菜单标记、Skip/Auto。</para>
/// <para>保留系统偏好（音量、文字速度、已读记录、CG 解锁等）。</para>
/// </summary>
public class ResetGameStateHandler : ICommandHandler<ResetGameStateCommand>, IDefaultCommandHandler
{
    public void Handle(ResetGameStateCommand _, ICommandContext ctx)
    {
        // 1. 停止 DSL 执行器
        ctx.DslExecutor?.Stop();

        // 2. 清除所有非系统、非局部变量
        foreach (var (k, _) in ctx.State.GetSnapshot())
        {
            if (!string.IsNullOrEmpty(k)
                && !k.StartsWith(StateKeys.SystemPrefix)
                && !k.StartsWith("_local_"))
                ctx.State.Remove(k);
        }

        // 3. 清除局部变量
        foreach (var (k, _) in ctx.State.GetSnapshot())
        {
            if (!string.IsNullOrEmpty(k) && k.StartsWith("_local_"))
                ctx.State.Remove(k);
        }

        // 4. 清除场景堆栈（back + forward）
        ctx.SceneStack?.Clear();

        // 5. 清除回溯检查点
        ctx.DslExecutor?.ClearCheckpoints();

        // 6. 清除菜单标记
        ctx.State.Set(StateKeys.Scene.MenuReturnTo, (string?)null);
        ctx.State.Set(StateKeys.Scene.GameDslIndex, 0);
        ctx.State.Set(StateKeys.Scene.GameDslWaitingType, "");
        ctx.State.Set(StateKeys.Scene.GameSceneElements, (List<UIElementEntity>?)null);
        ctx.State.Set(StateKeys.Scene.GameRuntimeElements, (List<UIElementEntity>?)null);
        ctx.State.Set(StateKeys.Scene.GameCurrentBackground, (string?)null);

        // 7. 重置 Skip/Auto 模式
        ctx.State.Set(StateKeys.Playback.SkipActive, false);
        ctx.State.Set(StateKeys.Playback.AutoActive, false);
        ctx.State.Set(StateKeys.Playback.AutoTimer, 0.0);

        // 8. 重置交互状态
        ctx.ResetInteractionState();

        // 9. Phase 60: 重置游戏时间 + 清空时间事件（小说世界模式）
        if (ctx.Options.EnableTimeSystem)
        {
            ctx.TimeService?.Reset();
            ctx.EventScheduler?.ClearEvents();
        }

        System.Diagnostics.Debug.WriteLine("[ResetGameStateHandler] 游戏状态已完全重置");
    }
}

/// <summary>深合并变量定义命令处理器</summary>
public class MergeDefinesHandler : ICommandHandler<MergeDefinesCommand>, IDefaultCommandHandler
{
    public void Handle(MergeDefinesCommand md, ICommandContext ctx)
        => MergeIntoState(md.Defines, ctx.State);

    private static void MergeIntoState(Dictionary<string, object?> dict, IStateContainer state, string prefix = "")
        => GameLoop.MergeIntoState(dict, state, prefix);
}

/// <summary>构建场景命令处理器</summary>
public class BuildSceneHandler : ICommandHandler<BuildSceneCommand>, IDefaultCommandHandler
{
    public void Handle(BuildSceneCommand bsc, ICommandContext ctx)
    {
        var elements = new List<UIElementEntity>();
        foreach (var raw in bsc.RawElements)
        {
            if (raw is UIElementEntity ui)
                elements.Add(ui);
        }
        if (!string.IsNullOrEmpty(bsc.SceneName))
            ctx.State.Set(StateKeys.Scene.CurrentName, bsc.SceneName);
        ctx.State.Set(StateKeys.Screen.ActiveScreen, bsc.SceneName);
        ctx.State.Set(StateKeys.Scene.Elements, elements);
        ctx.State.Set(StateKeys.Scene.Dirty, true);
    }
}
