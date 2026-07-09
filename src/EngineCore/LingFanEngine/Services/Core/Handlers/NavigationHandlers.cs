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
        //    label 跳转：不切换场景元素、不改场景名称、不清检查点
        //    仅重置交互状态（清除旧对话/菜单等），然后启动 DSL label 执行
        if (navEntity == null && ctx.StoryRegistry != null)
        {
            var labelFile = ctx.StoryRegistry.FindFileByLabel(navScName);
            if (labelFile != null)
            {
                ctx.ResetInteractionState();

                // 从 Menu/UI 导航到 label = 进入游戏流程（label 包含对话/选择等游戏内容）
                // 必须设为 Game 类型，否则 CreateCheckpoint 会跳过（不创建检查点）
                // 这修复了：标题→prologue label→town_entrance 链路中 prologue 不创建检查点的问题
                var labelCurType = (SceneType)ctx.State.Get<int>(StateKeys.Scene.CurrentType);
                if (labelCurType != SceneType.Game)
                    ctx.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);

                // 不改 Scene.CurrentName —— SceneView 通过它判断是否重建场景
                // 保留当前场景名意味着 UI 元素（按钮/背景图）保持不变
                ctx.DslExecutor?.StartFromLabel(navScName);
                return;
            }
        }

        var sceneType = navEntity?.SceneType ?? SceneType.Game;

        // ========== 按 SceneType 分流：Game 完整切换，Menu/UI 不侵入 ==========
        var (curType, menuReturnTo) = ApplyNavigationStateTransition(ctx, sceneType, navScName, curSc);
        // isMenuReturn 仅在目标为 Game 场景时生效——Menu→Menu 导航不应误判为返回
        var isMenuReturn = sceneType == SceneType.Game
            && curType == SceneType.Menu
            && !string.IsNullOrEmpty(menuReturnTo)
            && navScName == menuReturnTo;

        // 4. 有场景实体 → 加载场景元素 + Defines
        if (navEntity != null)
        {
            // 深合并场景级 Defines（补缺+修类型，等价于 C# StoryScript.InDefines）
            if (navEntity.Defines != null && navEntity.Defines.Count > 0)
                MergeIntoState(navEntity.Defines, ctx.State);

            ctx.State.Set(StateKeys.Scene.CurrentName, navScName);
            ctx.State.Set(StateKeys.Screen.ActiveScreen, navScName);

            if (!isMenuReturn)
            {
                // 新场景或 Game→Game：清空元素，由 DSL 重建
                ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>());
            }
            // else: Menu→Game 返回，元素已由 ApplyNavigationStateTransition 从 __game_scene_elements 恢复

            // 同名导航时场景名不变，SceneView 不会检测到变化，必须设 Dirty 触发重建
            if (navScName == curSc || isMenuReturn)
                ctx.State.Set(StateKeys.Scene.Dirty, true);

            // 启动 DSL 执行器
            if (ctx.DslExecutor != null && ctx.StoryRegistry != null)
            {
                var preserveCps = curType == SceneType.Game || isMenuReturn;

                if (isMenuReturn)
                {
                    // Menu→Game 返回：从保存的 DSL 位置恢复执行
                    var savedIdx = ctx.State.Get<int>(StateKeys.Scene.GameDslIndex);
                    var (cmds, lbls) = ctx.StoryRegistry.GetCompiledResult(navScName);
                    if (cmds != null && lbls != null)
                    {
                        ctx.DslExecutor.LoadCommands(cmds, lbls, preserveCps);
                        // 回放场景构建命令重建元素（与存档恢复逻辑一致）
                        var replayElements = new List<UIElementEntity>();
                        string? replayBg = null;
                        for (int i = 0; i < savedIdx && i < cmds.Count; i++)
                        {
                            if (cmds[i] is ShowElementCommand se)
                                replayElements.Add(se.Element);
                            else if (cmds[i] is ShowHideCommand sh && sh.IsBackground && sh.IsShow)
                                replayBg = sh.Target;
                        }
                        ctx.State.Set(StateKeys.Scene.Elements, replayElements);
                        if (replayBg != null)
                            ctx.State.Set(StateKeys.Scene.CurrentBackground, replayBg);
                        ctx.State.Set(StateKeys.Dsl.CurrentIndex, savedIdx);
                        ctx.DslExecutor.Start();
                        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] Menu→Game 返回: 从索引 {savedIdx} 恢复");
                    }
                }
                else if (nc.EntryLabel != null)
                {
                    TryStartEntryLabel(navScName, nc.EntryLabel, ctx, preserveCps);
                }
                else
                {
                    var (cmds, lbls) = ctx.StoryRegistry.GetCompiledResult(navScName);
                    if (cmds != null && lbls != null && lbls.ContainsKey(navScName))
                    {
                        ctx.DslExecutor.LoadCommands(cmds, lbls, preserveCps);
                        ctx.DslExecutor.StartFromLabel(navScName);
                        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 启动场景同名 label: {navScName}");
                    }
                }

                // 清除 Menu 来源标记（已使用完毕）
                ctx.State.Set(StateKeys.Scene.GameDslIndex, 0);
                ctx.State.Set(StateKeys.Scene.GameDslWaitingType, "");
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
ctx.State.Set(StateKeys.Screen.ActiveScreen, navScName);
System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 启动 StoryScript [{navScName}] type={sceneType}");

        // 深合并场景变量定义（补缺+修类型）
        if (scriptEntry.Defines != null)
            MergeIntoState(scriptEntry.Defines, ctx.State);

        // Game 类型 C# 场景：创建场景级检查点，纳入统一回溯时间线
        // C# 场景没有 DSL 逐句检查点，回溯到此处 = 重新执行整个 StoryScript.Run()
        // CreateSceneCheckpoint 内部已推进回溯前沿
        if (sceneType == SceneType.Game && ctx.DslExecutor != null)
        {
            ctx.DslExecutor.CreateSceneCheckpoint(navScName);
        }

        _ = RunScriptEntryWithGeneration(scriptEntry, ctx.State);
    }

    /// <summary>
    /// 启动 C# StoryScript Runner，设置 AsyncLocal 回放代次以支持回溯取消
    /// </summary>
    private static async Task RunScriptEntryWithGeneration(SceneScriptEntry scriptEntry, IStateContainer state)
    {
        var gen = state.Get<int>(StateKeys.Dsl.CSharpReplayGeneration);
        GameController.CSharpReplayGen.Value = gen;
        try
        {
            await scriptEntry.Runner();
        }
        catch (CSharpSceneReplayCancelledException)
        {
            // 回溯/前进取消了此场景——Runner 已被异常终止
            System.Diagnostics.Debug.WriteLine($"[NavigateHandler] C# 场景 [{scriptEntry.SceneName}] 被回溯/前进取消");
        }
        finally
        {
            GameController.CSharpReplayGen.Value = 0;
        }
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
            // GameDslIndex/GameDslWaitingType 由调用方（NavigateHandler）使用后再清除
            // 恢复游戏场景元素（深拷贝——避免引用共享导致后续 DSL 修改影响 __game_scene_elements）
            var savedElements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.GameSceneElements);
            if (savedElements != null)
            {
                ctx.State.Set(StateKeys.Scene.Elements, new List<UIElementEntity>(savedElements));
                ctx.State.Set(StateKeys.Scene.Dirty, true);
            }
            var savedRuntime = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.GameRuntimeElements);
            if (savedRuntime != null)
                ctx.State.Set(StateKeys.Scene.RuntimeElements, new List<UIElementEntity>(savedRuntime));
            var savedBg = ctx.State.Get<string>(StateKeys.Scene.GameCurrentBackground);
            if (savedBg != null)
                ctx.State.Set(StateKeys.Scene.CurrentBackground, savedBg);
            ctx.State.Set(StateKeys.Scene.GameSceneElements, (List<UIElementEntity>?)null);
            ctx.State.Set(StateKeys.Scene.GameRuntimeElements, (List<UIElementEntity>?)null);
            ctx.State.Set(StateKeys.Scene.GameCurrentBackground, (string?)null);
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
                // 保存游戏 DSL 执行位置（LoadCommands 会重置 CurrentIndex=0）
                ctx.State.Set(StateKeys.Scene.GameDslIndex,
                    ctx.State.Get<int>(StateKeys.Dsl.CurrentIndex));
                ctx.State.Set(StateKeys.Scene.GameDslWaitingType,
                    ctx.State.Get<string>(StateKeys.Dsl.WaitingType) ?? "");
                // 保存游戏场景元素（深拷贝——避免引用共享，后续 DSL 追加元素不会污染此备份）
                var gameElements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.Elements);
                ctx.State.Set(StateKeys.Scene.GameSceneElements, gameElements != null ? new List<UIElementEntity>(gameElements) : null);
                var gameRuntime = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements);
                ctx.State.Set(StateKeys.Scene.GameRuntimeElements, gameRuntime != null ? new List<UIElementEntity>(gameRuntime) : null);
                ctx.State.Set(StateKeys.Scene.GameCurrentBackground,
                    ctx.State.Get<string>(StateKeys.Scene.CurrentBackground) ?? "");
            }
            else
            {
                // Menu→Menu 或 UI→Menu 导航：清除过期的 MenuReturnTo
                // 防止后续 Menu→Game 导航误判为返回旧来源
                ctx.State.Set(StateKeys.Scene.MenuReturnTo, (string?)null);
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

        // 从 Menu/UI 导航到 label = 进入游戏流程，设为 Game 类型
        var curType = (SceneType)ctx.State.Get<int>(StateKeys.Scene.CurrentType);
        if (curType != SceneType.Game)
            ctx.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);

        if (ctx.DslExecutor == null) return;

ctx.State.Set(StateKeys.Scene.CurrentName, navScName);
ctx.State.Set(StateKeys.Screen.ActiveScreen, navScName);

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

/// <summary>
/// 时间线回退命令处理器（鼠标滚轮上滚）
/// <para>在 DSL 检查点列表中向后移动一步，显示上一个交互点的完整状态。</para>
/// <para>跨场景：检查点包含全量状态快照，恢复时自动还原场景命令和元素。</para>
/// <para>检查点耗尽时直接返回——不回退到 SceneStack。</para>
/// </summary>
public class RollbackHandler : ICommandHandler<RollbackCommand>, IDefaultCommandHandler
{
    public void Handle(RollbackCommand rc, ICommandContext ctx)
    {
        if (ctx.DslExecutor != null && ctx.DslExecutor.CanRollback())
            ctx.DslExecutor.Rollback();
    }
}

/// <summary>
/// 时间线前进命令处理器（鼠标滚轮下滚）
/// <para>在 DSL 检查点列表中向前移动一步，恢复下一个交互点的完整状态。</para>
/// <para>只有回退过后才能前进——到达时间线前沿后此命令无效。</para>
/// </summary>
public class RollforwardHandler : ICommandHandler<RollforwardCommand>, IDefaultCommandHandler
{
    public void Handle(RollforwardCommand rc, ICommandContext ctx)
    {
        if (ctx.DslExecutor != null && ctx.DslExecutor.CanRollforward())
            ctx.DslExecutor.Rollforward();
    }
}

/// <summary>
/// 场景级回退/前进后重新加载 DSL 场景
/// <para>从 StoryRegistry 加载场景命令列表，从场景同名 label 启动 DSL。</para>
/// <para>DslExecutor 会按序执行 ShowElementCommand 重建场景元素。</para>
/// </summary>
internal static class SceneStackDslRestarter
{
    public static void Restart(string sceneName, ICommandContext ctx)
    {
        if (ctx.DslExecutor == null || ctx.StoryRegistry == null)
            return;

        if (!ctx.StoryRegistry.LoadScene(sceneName))
            return;

        var (cmds, lbls) = ctx.StoryRegistry.GetCompiledResult(sceneName);
        if (cmds == null || lbls == null)
            return;

        if (lbls.TryGetValue(sceneName, out _))
        {
            // 场景级 Back/Forward 是硬跳转——清空 DSL 检查点
            ctx.DslExecutor.LoadCommands(cmds, lbls, preserveCheckpoints: false);
            ctx.DslExecutor.StartFromLabel(sceneName);
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

        System.Diagnostics.Debug.WriteLine("[ResetGameStateHandler] 游戏状态已完全重置");
    }
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
ctx.State.Set(StateKeys.Screen.ActiveScreen, bsc.SceneName);
        ctx.State.Set(StateKeys.Scene.Elements, elements);
        ctx.State.Set(StateKeys.Scene.Dirty, true);
    }
}
