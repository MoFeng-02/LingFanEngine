using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Core;
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
            {
                MergeIntoState(navEntity.Defines, ctx.State);
                foreach (var (dk, dv) in navEntity.Defines)
                    System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 场景 define 注入: {dk} = {dv} (type={dv?.GetType().Name})");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[NavigateHandler] 场景 [{navScName}] 无 Defines (Defines={navEntity.Defines?.Count ?? 0})");
            }

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
                        // 统一使用 SceneReplayHelper 处理所有运行时元素命令
                        var replayCount = SceneReplayHelper.ReplaySceneState(cmds, savedIdx, ctx.State);
                        ctx.State.Set(StateKeys.Dsl.CurrentIndex, savedIdx);
                        ctx.DslExecutor.Start();
                        System.Diagnostics.Debug.WriteLine($"[NavigateHandler] Menu→Game 返回: 从索引 {savedIdx} 恢复, {replayCount} 个场景元素");
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

        _ = RunScriptEntryWithGeneration(scriptEntry, ctx.State).ContinueWith(t =>
        {
            if (t.IsFaulted)
                System.Diagnostics.Debug.WriteLine($"[NavigateHandler] RunScriptEntry faulted: {t.Exception?.GetBaseException().Message}");
        }, TaskContinuationOptions.OnlyOnFaulted);
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
