using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 导航命令处理器 — 场景切换的核心逻辑
/// <para>优先级：SceneScriptEntry → SceneRegistry → StoryRegistry 懒加载 → DSL label 跳转</para>
/// </summary>
public class NavigateHandler : ICommandHandler<NavigateCommand>
{
    public void Handle(NavigateCommand nc, ICommandContext ctx)
    {
        // 场景切换时清空局部变量和交互状态（对标 Ren'Py scene 命令语义）
        ctx.ClearLocalVariables();
        ctx.ResetInteractionState();

        var navScName = nc.SceneName ?? nc.Path.TrimStart('/');
        if (navScName == "back_title") navScName = "title_main";

        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] targetScene={navScName}");

        var curSc = ctx.State.Get<string>(StateKeys.Scene.CurrentName);

        // 1. 优先尝试 SceneScriptEntry（C# StoryScript 注册的场景）
        if (ctx.TryGetScriptEntry(navScName, out var scriptEntry))
        {
            HandleScriptEntry(navScName, curSc, scriptEntry!, ctx);
            return;
        }

        // 2. 尝试 SceneRegistry + StoryRegistry 懒加载
        var navEntity = ctx.SceneRegistry?.FindScene(navScName);
        navEntity ??= (ctx.StoryRegistry?.LoadScene(navScName) ?? false)
            ? ctx.SceneRegistry?.FindScene(navScName) : null;

        var sceneType = navEntity?.SceneType ?? Abstractions.Entities.Enums.SceneType.Game;
        if (sceneType == Abstractions.Entities.Enums.SceneType.Game
            && !string.IsNullOrEmpty(curSc) && curSc != navScName)
            ctx.SceneStack?.Push(curSc);
        else if (ctx.Options.AutoClearStackOnMenu)
            ctx.SceneStack?.Clear();

        // 懒加载
        if (navEntity == null && ctx.StoryRegistry != null)
        {
            if (ctx.StoryRegistry.LoadScene(navScName))
            {
                navEntity = ctx.SceneRegistry?.FindScene(navScName);
                System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 懒加载场景 [{navScName}]: {navEntity != null}");
            }
        }

        // 3. 有 EntryLabel → 从 story 文件启动 DSL
        if (navEntity != null && nc.EntryLabel != null && ctx.DslExecutor != null && ctx.StoryRegistry != null)
        {
            TryStartEntryLabel(navScName, nc.EntryLabel, ctx);
        }

        // 4. 有场景实体 → 加载场景
        if (navEntity != null)
        {
            ctx.State.Set(StateKeys.Scene.CurrentName, navScName);
            ctx.State.Set(StateKeys.Scene.Elements, navEntity.Elements);
            System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 场景 [{navScName}] 已加载, 元素数={navEntity.Elements.Count}");

            // 执行场景入口脚本（EntryCommands）
            if (navEntity.EntryCommands != null && navEntity.EntryCommands.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 执行入口脚本: {navEntity.EntryCommands.Count} 条命令");
                foreach (var entryCmd in navEntity.EntryCommands)
                {
                    ctx.Pipeline.SendAsync(entryCmd);
                }
            }
            return;
        }

        // 5. 场景未注册 → 尝试从 DSL story 跳 label
        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] WARNING: 场景 [{navScName}] 未注册");
        TryLabelFallback(navScName, ctx);
    }

    private void HandleScriptEntry(string navScName, string? curSc, SceneScriptEntry scriptEntry, ICommandContext ctx)
    {
        if (scriptEntry.SceneType == Abstractions.Entities.Enums.SceneType.Game
            && !string.IsNullOrEmpty(curSc) && curSc != navScName)
            ctx.SceneStack?.Push(curSc);
        else if (ctx.Options.AutoClearStackOnMenu)
            ctx.SceneStack?.Clear();

        ctx.State.Set(StateKeys.Scene.CurrentName, navScName);
        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 启动 StoryScript [{navScName}]");

        // 深合并场景变量定义（补缺+修类型）
        if (scriptEntry.Defines != null)
            MergeIntoState(scriptEntry.Defines, ctx.State);
        _ = scriptEntry.Runner();
    }

    private void TryStartEntryLabel(string navScName, string entryLabel, ICommandContext ctx)
    {
        var (cmds, labels) = ctx.StoryRegistry!.GetCompiledResult(navScName);
        if (cmds != null && labels != null && labels.ContainsKey(entryLabel))
        {
            ctx.DslExecutor!.LoadCommands(cmds, labels);
            ctx.DslExecutor.StartFromLabel(entryLabel);
            System.Diagnostics.Debug.WriteLine(
                $"[NavigateHandler] EntryLabel={entryLabel}，启动 DSL 执行，命令数={cmds.Count}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[NavigateHandler] EntryLabel={entryLabel} 未找到编译结果或标签");
        }
    }

    private void TryLabelFallback(string navScName, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());

        if (ctx.DslExecutor == null) return;

        ctx.State.Set(StateKeys.Scene.CurrentName, navScName);

        // 先试当前已加载的 label
        ctx.DslExecutor.StartFromLabel(navScName);
        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 尝试跳转 label: {navScName}");

        // 如果当前 labels 中没有该 label，尝试从其他 story 文件懒加载
        if (ctx.StoryRegistry == null) return;

        var allFiles = ctx.StoryRegistry.GetAllStoryFiles();
        foreach (var filePath in allFiles)
        {
            if (ctx.StoryRegistry.LoadSceneFromFile(filePath))
            {
                var (cmds, lbls) = ctx.StoryRegistry.GetCompiledResultByFile(filePath);
                if (cmds != null && lbls != null)
                {
                    ctx.DslExecutor.LoadCommands(cmds, lbls);
                    ctx.DslExecutor.StartFromLabel(navScName);
                    System.Diagnostics.Debug.WriteLine(
                        $"[NavigateHandler] label 懒加载: {filePath} -> {navScName}, labels: {string.Join(", ", lbls.Keys)}");
                    break;
                }
            }
        }
    }

    /// <summary>深合并：委托到 GameLoop.MergeIntoState（统一实现）</summary>
    private static void MergeIntoState(Dictionary<string, object?> dict, IStateContainer state, string prefix = "")
        => GameLoop.MergeIntoState(dict, state, prefix);
}

/// <summary>
/// 后退命令处理器
/// <para>优先尝试 DSL Say 级回溯（同一场景内回退到上一句对话）。</para>
/// <para>无可用检查点时回退到 SceneStack 场景级导航。</para>
/// </summary>
public class BackHandler : ICommandHandler<BackCommand>
{
    public void Handle(BackCommand bc, ICommandContext ctx)
    {
        System.Diagnostics.Debug.WriteLine("[BackHandler]");

        // 1. 优先尝试 DSL Say 级回溯
        if (ctx.DslExecutor != null && ctx.DslExecutor.CanRollback())
        {
            ctx.DslExecutor.Rollback();
            return;
        }

        // 2. 回退到场景级导航（SceneStack）
        ctx.ResetInteractionState();
        var backSnapshot = ctx.SceneStack?.Back();
        if (backSnapshot != null)
        {
            var backEntity = ctx.SceneRegistry?.FindScene(backSnapshot.SceneName);
            if (backEntity != null)
            {
                ctx.State.Set(StateKeys.Scene.CurrentName, backSnapshot.SceneName);
                ctx.State.Set(StateKeys.Scene.Elements, backEntity.Elements);
            }
        }
    }
}

/// <summary>
/// 前进命令处理器
/// <para>优先尝试 DSL Say 级前进（回溯历史中前进到下一句对话）。</para>
/// <para>无可用检查点时回退到 SceneStack 场景级导航。</para>
/// </summary>
public class ForwardHandler : ICommandHandler<ForwardCommand>
{
    public void Handle(ForwardCommand fc, ICommandContext ctx)
    {
        System.Diagnostics.Debug.WriteLine("[ForwardHandler]");

        // 1. 优先尝试 DSL Say 级前进
        if (ctx.DslExecutor != null && ctx.DslExecutor.CanRollforward())
        {
            ctx.DslExecutor.Rollforward();
            return;
        }

        // 2. 回退到场景级导航（SceneStack）
        ctx.ResetInteractionState();
        var fwdSnapshot = ctx.SceneStack?.Forward();
        if (fwdSnapshot != null)
        {
            var fwdEntity = ctx.SceneRegistry?.FindScene(fwdSnapshot.SceneName);
            if (fwdEntity != null)
            {
                ctx.State.Set(StateKeys.Scene.CurrentName, fwdSnapshot.SceneName);
                ctx.State.Set(StateKeys.Scene.Elements, fwdEntity.Elements);
            }
        }
    }
}

/// <summary>场景命令处理器 — 清栈 + 切场景</summary>
public class SceneHandler : ICommandHandler<SceneCommand>
{
    public void Handle(SceneCommand sc, ICommandContext ctx)
    {
        ctx.ClearLocalVariables();
        ctx.ResetInteractionState();
        System.Diagnostics.Debug.WriteLine($"[SceneHandler] SceneName={sc.SceneName}");
        ctx.SceneStack?.Clear();
        var sceneEntity = ctx.SceneRegistry?.FindScene(sc.SceneName);
        if (sceneEntity != null)
        {
            ctx.State.Set(StateKeys.Scene.CurrentName, sc.SceneName);
            ctx.State.Set(StateKeys.Scene.Elements, sceneEntity.Elements);
        }
        else
        {
            ctx.State.Set(StateKeys.Scene.CurrentName, sc.SceneName);
            ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
        }
    }
}

/// <summary>导航到 DSL label 命令处理器</summary>
public class NavToLabelHandler : ICommandHandler<NavToLabelCommand>
{
    public void Handle(NavToLabelCommand ntl, ICommandContext ctx)
    {
        System.Diagnostics.Debug.WriteLine($"[NavToLabelHandler] TargetLabel={ntl.TargetLabel}");
        ctx.DslExecutor?.StartFromLabel(ntl.TargetLabel);
    }
}

/// <summary>清空场景堆栈命令处理器</summary>
public class ClearStackHandler : ICommandHandler<ClearStackCommand>
{
    public void Handle(ClearStackCommand command, ICommandContext ctx)
        => ctx.SceneStack?.Clear();
}

/// <summary>深合并变量定义命令处理器</summary>
public class MergeDefinesHandler : ICommandHandler<MergeDefinesCommand>
{
    public void Handle(MergeDefinesCommand md, ICommandContext ctx)
        => MergeIntoState(md.Defines, ctx.State);

    private static void MergeIntoState(Dictionary<string, object?> dict, IStateContainer state, string prefix = "")
        => GameLoop.MergeIntoState(dict, state, prefix);
}

/// <summary>构建场景命令处理器</summary>
public class BuildSceneHandler : ICommandHandler<BuildSceneCommand>
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
        ctx.State.Set(StateKeys.Scene.Elements, elements);
    }
}
