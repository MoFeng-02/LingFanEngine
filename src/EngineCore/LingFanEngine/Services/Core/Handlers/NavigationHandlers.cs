using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
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
public class NavigateHandler : ICommandHandler<NavigateCommand>, IDefaultCommandHandler
{
    public void Handle(NavigateCommand nc, ICommandContext ctx)
    {
        var navScName = nc.SceneName ?? nc.Path.TrimStart('/');
        if (navScName == ctx.Options.BackTitleAlias) navScName = ctx.Options.TitleSceneName;

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

        // 懒加载（如果还没找到）
        if (navEntity == null && ctx.StoryRegistry != null)
        {
            if (ctx.StoryRegistry.LoadScene(navScName))
            {
                navEntity = ctx.SceneRegistry?.FindScene(navScName);
                System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 懒加载场景 [{navScName}]: {navEntity != null}");
            }
        }

        // 3. 场景未找到 → 先检查是否为已知 label（避免误触发场景状态过渡）
        //    label 跳转：不切换场景元素、不改场景类型/名称、不清检查点
        //    仅重置交互状态（清除旧对话/菜单等），然后启动 DSL label 执行
        if (navEntity == null && ctx.StoryRegistry != null)
        {
            var labelFile = ctx.StoryRegistry.FindFileByLabel(navScName);
            if (labelFile != null)
            {
                // 是 label 不是场景 → 保留当前场景元素和类型，仅重置交互状态
                System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 跳转 label: {navScName} (file: {labelFile})");
                ctx.ResetInteractionState();
                // 不改 Scene.CurrentName —— SceneView 通过它判断是否重建场景
                // 保留当前场景名意味着 UI 元素（按钮/背景图）保持不变
                ctx.DslExecutor?.StartFromLabel(navScName);
                return;
            }
        }

        var sceneType = navEntity?.SceneType ?? SceneType.Game;

        // ========== 按 SceneType 分流：Game 完整切换，Menu/UI 不侵入 ==========
        var (curType, menuReturnTo) = ApplyNavigationStateTransition(ctx, sceneType, navScName, curSc);

        // 4. 有场景实体 → 加载场景元素 + Defines
        if (navEntity != null)
        {
            // 深合并场景级 Defines（补缺+修类型，等价于 C# StoryScript.InDefines）
            if (navEntity.Defines != null && navEntity.Defines.Count > 0)
                MergeIntoState(navEntity.Defines, ctx.State);

            ctx.State.Set(StateKeys.Scene.CurrentName, navScName);
            // 元素列表初始化为空——UI 元素现在由 DslExecutor 通过 ShowElementCommand 按序追加
            // 使用新实例避免共享 SceneEntity.Elements 引用
            // 不设 Dirty——场景名变更已触发 SceneView.RebuildScene
            ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
            System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 场景 [{navScName}] 已加载, type={sceneType}, 元素数=0 (由DSL按序追加), defines={navEntity.Defines?.Count ?? 0}");

            // 启动 DSL 执行器（互斥：优先 EntryLabel，其次场景同名 label）
            if (ctx.DslExecutor != null && ctx.StoryRegistry != null)
            {
                var preserveCps = curType == SceneType.Game ||
                    (curType == SceneType.Menu && !string.IsNullOrEmpty(menuReturnTo) && navScName == menuReturnTo);

                if (nc.EntryLabel != null)
                {
                    // 显式 EntryLabel → 从指定 label 启动
                    TryStartEntryLabel(navScName, nc.EntryLabel, ctx, preserveCps);
                }
                else
                {
                    // 无 EntryLabel → 尝试场景同名 label（scene 块内的流程命令已转为 label）
                    var (cmds, lbls) = ctx.StoryRegistry.GetCompiledResult(navScName);
                    if (cmds != null && lbls != null && lbls.ContainsKey(navScName))
                    {
                        ctx.DslExecutor.LoadCommands(cmds, lbls, preserveCps);
                        ctx.DslExecutor.StartFromLabel(navScName);
                        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 启动场景同名 label: {navScName}");
                    }
                }
            }
            return;
        }

        // 5. 既不是场景也不是已知 label → 最后回退
        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] WARNING: 目标 [{navScName}] 未注册为场景或 label");
        TryLabelFallback(navScName, ctx);
    }

    private void HandleScriptEntry(string navScName, string? curSc, SceneScriptEntry scriptEntry, ICommandContext ctx)
    {
        var sceneType = scriptEntry.SceneType;
        ApplyNavigationStateTransition(ctx, sceneType, navScName, curSc);

        ctx.State.Set(StateKeys.Scene.CurrentName, navScName);
        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 启动 StoryScript [{navScName}] type={sceneType}");

        // 深合并场景变量定义（补缺+修类型）
        if (scriptEntry.Defines != null)
            MergeIntoState(scriptEntry.Defines, ctx.State);
        _ = scriptEntry.Runner();
    }

    /// <summary>
    /// 统一的场景切换状态过渡逻辑（Handle 和 HandleScriptEntry 共用）
    /// <para>根据当前场景类型和目标场景类型，决定检查点/局部变量/堆栈的保留或清除。</para>
    /// </summary>
    /// <returns>(过渡前的场景类型, 过渡前的 MenuReturnTo 值)</returns>
    private static (SceneType curType, string? menuReturnTo) ApplyNavigationStateTransition(
        ICommandContext ctx, SceneType targetSceneType, string navScName, string? curSc)
    {
        var curType = (SceneType)ctx.State.Get<int>(StateKeys.Scene.CurrentType);
        var menuReturnTo = ctx.State.Get<string>(StateKeys.Scene.MenuReturnTo);

        if (targetSceneType == SceneType.Game)
        {
            if (curType == SceneType.Menu && !string.IsNullOrEmpty(menuReturnTo) && navScName == menuReturnTo)
            {
                // Menu → Game（返回）：保留一切（检查点 + 局部变量），只重置交互状态
                ctx.ResetInteractionState();
            }
            else if (curType == SceneType.Menu)
            {
                // Menu → Game（新游戏/不同场景）：清空一切，全新开始
                ctx.ClearLocalVariables();
                ctx.DslExecutor?.ClearCheckpoints();
                ctx.ResetInteractionState();
            }
            else
            {
                // Game → Game：保留检查点（跨场景回溯），只清局部变量
                ctx.ClearLocalVariables();
                ctx.ResetInteractionState();

                if (!string.IsNullOrEmpty(curSc) && curSc != navScName)
                    ctx.SceneStack?.Push(curSc);
            }

            // 清除 Menu/UI 来源标记
            ctx.State.Set(StateKeys.Scene.MenuReturnTo, (string?)null);
        }
        else
        {
            // Menu/UI 场景：不侵入游戏状态
            ctx.ResetInteractionState();     // 只清交互状态（对话框等）
            // 不调 ClearLocalVariables！保留检查点和局部变量
            // 不 Push 到 SceneStack
            if (ctx.Options.AutoClearStackOnMenu)
                ctx.SceneStack?.Clear();

            // 记住来源场景（仅当从 Game 场景进入 Menu/UI 时记录）
            if (curType == SceneType.Game
                && !string.IsNullOrEmpty(curSc) && curSc != navScName)
            {
                ctx.State.Set(StateKeys.Scene.MenuReturnTo, curSc);
            }
        }

        // 写入当前场景类型
        ctx.State.Set(StateKeys.Scene.CurrentType, (int)targetSceneType);

        return (curType, menuReturnTo);
    }

    private void TryStartEntryLabel(string navScName, string entryLabel, ICommandContext ctx, bool preserveCheckpoints = false)
    {
        var (cmds, labels) = ctx.StoryRegistry!.GetCompiledResult(navScName);
        if (cmds != null && labels != null && labels.ContainsKey(entryLabel))
        {
            ctx.DslExecutor!.LoadCommands(cmds, labels, preserveCheckpoints);
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

        // 检查 StartFromLabel 是否成功（Executing 被设为 true 表示找到了 label）
        if (ctx.State.Get<bool>(StateKeys.Dsl.Executing))
            return;

        // 当前 labels 中没有该 label，尝试从其他 story 文件懒加载
        if (ctx.StoryRegistry == null) return;

        var allFiles = ctx.StoryRegistry.GetAllStoryFiles();
        foreach (var filePath in allFiles)
        {
            if (ctx.StoryRegistry.LoadSceneFromFile(filePath))
            {
                var (cmds, lbls) = ctx.StoryRegistry.GetCompiledResultByFile(filePath);
                // 只有当文件包含目标 label 时才加载（避免覆写正确的命令列表）
                if (cmds != null && lbls != null && lbls.ContainsKey(navScName))
                {
                    ctx.DslExecutor.LoadCommands(cmds, lbls, preserveCheckpoints: true);
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
public class BackHandler : ICommandHandler<BackCommand>, IDefaultCommandHandler
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
                // 元素列表初始化为空——由 DslExecutor 按序追加（场景级回退后需手动重启 DSL）
                ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
                ctx.State.Set(StateKeys.Scene.Dirty, true);
            }
        }
    }
}

/// <summary>
/// 前进命令处理器
/// <para>优先尝试 DSL Say 级前进（回溯历史中前进到下一句对话）。</para>
/// <para>无可用检查点时回退到 SceneStack 场景级导航。</para>
/// </summary>
public class ForwardHandler : ICommandHandler<ForwardCommand>, IDefaultCommandHandler
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
                // 元素列表初始化为空——由 DslExecutor 按序追加
                ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
                ctx.State.Set(StateKeys.Scene.Dirty, true);
            }
        }
    }
}

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
        }
        else
        {
            ctx.ResetInteractionState();
        }

        ctx.State.Set(StateKeys.Scene.CurrentType, (int)sceneType);
        System.Diagnostics.Debug.WriteLine($"[SceneHandler] SceneName={sc.SceneName} type={sceneType}");

        if (sceneEntity != null)
        {
            // 深合并场景级 Defines
            if (sceneEntity.Defines != null && sceneEntity.Defines.Count > 0)
                GameLoop.MergeIntoState(sceneEntity.Defines, ctx.State);

            ctx.State.Set(StateKeys.Scene.CurrentName, sc.SceneName);
            // 元素列表初始化为空——UI 元素现在由 DslExecutor 通过 ShowElementCommand 按序追加
            // 不设 Dirty——场景名变更已触发 SceneView.RebuildScene
            ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
        }
        else
        {
            ctx.State.Set(StateKeys.Scene.CurrentName, sc.SceneName);
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

/// <summary>导航到 DSL label 命令处理器</summary>
public class NavToLabelHandler : ICommandHandler<NavToLabelCommand>, IDefaultCommandHandler
{
    public void Handle(NavToLabelCommand ntl, ICommandContext ctx)
    {
        System.Diagnostics.Debug.WriteLine($"[NavToLabelHandler] TargetLabel={ntl.TargetLabel}");
        ctx.DslExecutor?.StartFromLabel(ntl.TargetLabel);
    }
}

/// <summary>清空场景堆栈命令处理器</summary>
public class ClearStackHandler : ICommandHandler<ClearStackCommand>, IDefaultCommandHandler
{
    public void Handle(ClearStackCommand command, ICommandContext ctx)
        => ctx.SceneStack?.Clear();
}

/// <summary>
/// 回溯到指定检查点命令处理器（从历史面板跳转）
/// </summary>
public class RollbackToHandler : ICommandHandler<RollbackToCommand>, IDefaultCommandHandler
{
    public void Handle(RollbackToCommand rtc, ICommandContext ctx)
    {
        if (ctx.DslExecutor == null) return;

        var targetPos = rtc.TargetCheckpointIndex;
        if (targetPos < 0)
        {
            // 等价于 Back
            ctx.DslExecutor.Rollback();
            return;
        }

        ctx.DslExecutor.RollbackTo(targetPos);
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
        ctx.State.Set(StateKeys.Scene.Elements, elements);
        ctx.State.Set(StateKeys.Scene.Dirty, true);
    }
}
