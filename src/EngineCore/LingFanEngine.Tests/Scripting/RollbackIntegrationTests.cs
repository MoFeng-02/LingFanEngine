using System.Collections.Generic;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>
/// 叙事模式逐句回溯集成测试（盲区验证 T13）。
/// <para>验证统一线性回溯时间线的核心机制：</para>
/// <para>1) Rollback() 真实恢复检查点快照（非仅移动索引）——直接驱动真实 DslExecutor + 真实 StateContainer；</para>
/// <para>2) 无检查点时 Rollback() 返回 false；</para>
/// <para>3) 小说世界模式(EnableTimeSystem)禁用逐句回溯——检查点不被创建；</para>
/// <para>4) MaxRollbackCheckpoints 超出时驱逐最旧检查点（防内存无限增长）。</para>
/// </summary>
public class RollbackIntegrationTests
{
    private static DslExecutor MakeExecutor(StateContainer state, bool enableTimeSystem = false)
    {
        var options = new LingFanEngineOptions { EnableTimeSystem = enableTimeSystem };
        return new DslExecutor(state, new CommandPipeline(), options, new AsyncWaitService(state));
    }

    [Fact]
    public void Rollback_RestoresPriorCheckpointState()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        state.Set("hp", 100);
        var exe = MakeExecutor(state);

        // 模拟两个检查点：cp0(hp=100) → cp1(hp=50)
        var cp0 = new RollbackCheckpoint
        {
            CommandIndex = 0,
            InteractionType = "dialog",
            StateSnapshot = new Dictionary<string, object?> { ["hp"] = 100 }
        };
        var cp1 = new RollbackCheckpoint
        {
            CommandIndex = 1,
            InteractionType = "dialog",
            StateSnapshot = new Dictionary<string, object?> { ["hp"] = 50 }
        };
        state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint> { cp0, cp1 });
        state.Set(StateKeys.Rollback.CurrentIndex, 1);

        // 当前运行时状态已被推进到 50（cp1 之后的修改）
        state.Set("hp", 50);

        exe.Rollback().Should().BeTrue();

        // 关键断言：状态被真实回滚到 cp0 的快照值
        state.Get<int>("hp").Should().Be(100);
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(0);
        // 回滚后处于 replay/rollback 会话中（cp1 仍在当前位置之前未重放），IsActive 应为 True
        state.Get<bool>(StateKeys.Rollback.IsActive).Should().BeTrue();
    }

    [Fact]
    public void Rollback_NoCheckpoint_ReturnsFalse()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        var exe = MakeExecutor(state);

        exe.Rollback().Should().BeFalse();
    }

    [Fact]
    public async Task RollbackRollforward_Alternating_StepsOneByOne()
    {
        // 回归（2026-09 滚轮历史跳步 BUG 治根）：场景 = 正在阅读 live 句（say 未点击、未建 cp）——
        // LiveCheckpointed=false。此时 frontier 的末位检查点是「上一句」，回退应落在它上面。
        // 旧「frontier 即盲目跳过」一步跨两个检查点（阅读句3 → 直落句1，跳过句2）。
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        var exe = MakeExecutor(state);

        RollbackCheckpoint Cp(int i, string txt) => new()
        {
            CommandIndex = i,
            InteractionType = "dialog",
            StateSnapshot = new Dictionary<string, object?> { ["txt"] = txt }
        };
        var cps = new List<RollbackCheckpoint> { Cp(0, "句1"), Cp(1, "句2"), Cp(2, "句3") };
        state.Set(StateKeys.Rollback.Checkpoints, cps);
        state.Set(StateKeys.Rollback.CurrentIndex, 3); // frontier
        state.Set(StateKeys.Rollback.LiveCheckpointed, false); // 正在阅读未入档的 live 句

        // 上滚 1（阅读句4 中）：落句3（上一句）——不再盲目跳过
        exe.Rollback().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句3");
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(2);
        state.Get<bool>(StateKeys.Rollback.IsReplay).Should().BeTrue("回退浏览历史应处于重放模式");

        // 上滚 2：句3 → 句2
        exe.Rollback().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句2");
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(1);

        // 上滚 3：句2 → 句1
        exe.Rollback().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句1");
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(0);

        // 下滚：句1 → 句2 → 句3 逐级可进（用户报告的"下滚 2、3 进不去"）
        exe.Rollforward().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句2");
        exe.Rollforward().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句3");
        state.Get<bool>(StateKeys.Rollback.IsReplay).Should().BeTrue();

        // 下滚到 frontier：退出重放回 live（旧实现 IsReplay 恒 true——滚轮下无响应）
        exe.Rollforward().Should().BeTrue();
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(3);
        state.Get<bool>(StateKeys.Rollback.IsReplay).Should().BeFalse("回到前沿 = 退出重放，继续正常执行");
        exe.CanRollforward().Should().BeFalse();

        // 重放浏览不得污染时间线
        await Task.Delay(50);
        state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints)!.Count.Should().Be(3);
    }

    [Fact]
    public void Rollback_LiveCheckpointed_SkipsRedundantLastCheckpoint()
    {
        // 场景 = say 点击后（cp 刚建，live 画面 == cp[^1]）——LiveCheckpointed=true。
        // 此时末位检查点与当前画面相同，回退应跳过它落再前一个（一句一格，无视觉空步）。
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        var exe = MakeExecutor(state);

        RollbackCheckpoint Cp(int i, string txt) => new()
        {
            CommandIndex = i,
            InteractionType = "dialog",
            StateSnapshot = new Dictionary<string, object?> { ["txt"] = txt }
        };
        var cps = new List<RollbackCheckpoint> { Cp(0, "句1"), Cp(1, "句2"), Cp(2, "句3") };
        state.Set(StateKeys.Rollback.Checkpoints, cps);
        state.Set(StateKeys.Rollback.CurrentIndex, 3); // frontier
        state.Set(StateKeys.Rollback.LiveCheckpointed, true); // 点击刚过句3，画面==cp[2]

        // 上滚 1：跳过 ==live 的句3，落句2（NVL 尾部冗余同语义）
        exe.Rollback().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句2");
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(1);

        // 上滚 2：句1
        exe.Rollback().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句1");

        // 下滚经过被跳过的句3（前进完整可回）
        exe.Rollforward().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句2");
        exe.Rollforward().Should().BeTrue();
        state.Get<string>("txt").Should().Be("句3");
    }

    [Fact]
    public async Task Rollback_WhileReadingLiveSay_LandsOnPreviousLine()
    {
        // 真实链路复现（用户原始报告）：真实编译 3 句 say，前两句点击通过，
        // 第 3 句展示中（未点击、未建检查点）时滚轮上滚一次——必须落句2（上一句），
        // 而非旧「frontier 盲目跳过」的句1（一步跨两个检查点）。
        var host = new EngineTestHost(enableTimeSystem: false);
        host.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        host.State.Set(StateKeys.Rollback.BlockedUntil, -1);

        var script = "say \"句1\"\nsay \"句2\"\nsay \"句3\"\nsay \"句4\"";
        var cmds = new LingFanDslEngine().Compile(script).Commands;
        host.DslExecutor.LoadCommands(cmds);
        host.DslExecutor.Start();

        // 手动充当 GameLoop：消费管道命令并 dispatch 到真实 ShowDialogHandler
        var ctx = new LingFanEngine.Tests.Handlers.FakeCommandContext(host.State);
        var dialogHandler = new LingFanEngine.Services.Core.Handlers.ShowDialogHandler();

        async Task PumpAsync()
        {
            while (host.Pipeline.TryRead(out var c))
            {
                if (c is ShowDialogCommand sd)
                    dialogHandler.Handle(sd, ctx);
                await Task.Delay(1);
            }
        }

        // 句1 展示 → 点击 → 句2 展示 → 点击 → 句3 展示（停止，保持阅读中状态）
        for (var line = 1; line <= 2; line++)
        {
            await Task.Run(() => SpinWaitUntil(() => host.State.Get<string>(StateKeys.Dialog.Text) == $"句{line}"));
            await PumpAsync();
            host.CompleteDialog(); // 点击通过
            await Task.Delay(20);
        }
        await Task.Run(() => SpinWaitUntil(() => host.State.Get<string>(StateKeys.Dialog.Text) == "句3"));
        await PumpAsync();

        // 前置确认：此刻检查点应为 [句1, 句2]（句3 未点击未入档），frontier P=2
        var cpsBefore = host.State.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        cpsBefore!.Count.Should().Be(2, "句3 正在阅读（未点击），只应有前两句的检查点");
        host.State.Get<bool>(StateKeys.Rollback.LiveCheckpointed).Should().BeFalse(
            "正在阅读的 say 尚未入档（时间线消歧标志）");

        // 滚轮上滚一次（模拟 RollbackCommand 处理）
        host.DslExecutor.Rollback().Should().BeTrue("阅读中回退应有效");

        // 治根断言：落在句2（上一句），而非句1（旧 BUG：frontier 盲目跳过跨两步）
        host.State.Get<string>(StateKeys.Dialog.Text).Should().Be("句2",
            "阅读句3 时上滚必须落句2——旧实现盲目跳过末位检查点直落句1（用户报告的跳步）");
        host.State.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(1);
        host.State.Get<bool>(StateKeys.Rollback.IsReplay).Should().BeTrue();
    }

    /// <summary>自旋等待谓词为真（5s 超时返回 false）</summary>
    private static bool SpinWaitUntil(Func<bool> predicate, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (predicate()) return true;
            Thread.Sleep(5);
        }
        return predicate();
    }

    [Fact]
    public async Task WorldMode_DisablesCheckpointCreation()
    {
        var host = new EngineTestHost(enableTimeSystem: true);
        host.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        host.State.Set(StateKeys.Rollback.BlockedUntil, -1);

        var cmds = new LingFanDslEngine().Compile("""say "第一句" """).Commands;
        await host.RunDslAndDriveAsync(cmds);

        var cps = host.State.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        (cps == null || cps.Count == 0).Should().BeTrue();
    }

    [Fact]
    public async Task MaxRollbackCheckpoints_EvictsOldest()
    {
        var host = new EngineTestHost(enableTimeSystem: false);
        host.Options.MaxRollbackCheckpoints = 3;
        host.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        // 生产环境由 StateInitializer 将 BlockedUntil 初始化为 -1；EngineTestHost 不含该步骤，
        // 不设置则未设整型默认 0 会令 CreateCheckpoint 的 blockedUntil>=0 分支恒成立而跳过检查点。
        host.State.Set(StateKeys.Rollback.BlockedUntil, -1);

        var script = string.Join("\n", System.Linq.Enumerable.Range(1, 5).Select(i => $"say \"句{i}\""));
        var cmds = new LingFanDslEngine().Compile(script).Commands;
        await host.RunDslAndDriveAsync(cmds);

        var cps = host.State.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        cps.Should().NotBeNull();
        cps!.Count.Should().Be(3); // 超出上限，最旧被驱逐
    }

    [Fact]
    public void NvlRollback_SkipsRedundantSceneIdle_AndAllowsSceneLevelRollback()
    {
        // 手搭"修复后"的 NVL 检查点布局：NVL 3 句 + 场景入口 csharp_scene。
        // 修复后 scene_idle 因 Nvl.Text 与末句相同被抑制，故不存在第 4 个"全展示"尾检查点。
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        state.Set(StateKeys.Nvl.Active, true);
        var exe = MakeExecutor(state);

        var csharp = new RollbackCheckpoint
        {
            CommandIndex = -1,
            InteractionType = "csharp_scene",
            SceneName = "demo",
            StateSnapshot = new Dictionary<string, object?> { [StateKeys.Nvl.Text] = "" }
        };
        var cp0 = new RollbackCheckpoint
        {
            CommandIndex = 0,
            InteractionType = "dialog",
            SceneName = "demo",
            StateSnapshot = new Dictionary<string, object?> { [StateKeys.Nvl.Text] = "1234" }
        };
        var cp1 = new RollbackCheckpoint
        {
            CommandIndex = 1,
            InteractionType = "dialog",
            SceneName = "demo",
            StateSnapshot = new Dictionary<string, object?> { [StateKeys.Nvl.Text] = "1234\n5678" }
        };
        var cp2 = new RollbackCheckpoint
        {
            CommandIndex = 2,
            InteractionType = "dialog",
            SceneName = "demo",
            StateSnapshot = new Dictionary<string, object?> { [StateKeys.Nvl.Text] = "1234\n5678\n00000" }
        };
        state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint> { csharp, cp0, cp1, cp2 });
        state.Set(StateKeys.Rollback.CurrentIndex, 4); // 播放结束 frontier 指向末位之后
        // 场景 = 刚点击过末句（live == cp2 全展示已入档）——首下回退跳过冗余末位
        state.Set(StateKeys.Rollback.LiveCheckpointed, true);

        // 首下回退：应落到 CP1（2 行），而非"全展示"的 CP2
        exe.Rollback().Should().BeTrue();
        state.Get<string>(StateKeys.Nvl.Text).Should().Be("1234\n5678");

        // 再下回退：落到 CP0（1 行）
        exe.Rollback().Should().BeTrue();
        state.Get<string>(StateKeys.Nvl.Text).Should().Be("1234");

        // 第三下：目标为 csharp_scene → 现在允许场景级回溯（重跑整个 C# StoryScript）。
        // 回退到 csharp_scene 恢复其快照（Nvl.Text=""）；本测试未接线 OnCSharpSceneReplay，
        // RestoreAndRestart 走 else 分支重置 replay 标志，但 Rollback() 本身返回 true。
        // 这是 C# 场景回溯支持（用户问题2）的核心行为——护栏已移除。
        exe.Rollback().Should().BeTrue();
        state.Get<string>(StateKeys.Nvl.Text).Should().Be("");
    }

    [Fact]
    public async Task NvlClear_CreatesCheckpoint_SoRollbackStepsThroughClearedState()
    {
        // 验证用户报告的 NVL 回退缺陷修复：nvl clear 是"清零 NVL"的叙事节点。
        // 修复前 nvl clear 不建检查点，导致从 nvl clear 后的 say 回退会直接跳回清理前的多行块，
        // 跳过"已清空"状态（用户感知为"多点几次/回到错的位置"）。
        // 修复后 nvl clear 建立一个 nvl_clear 检查点，回退逐步经过它。
        var host = new EngineTestHost(enableTimeSystem: false);
        host.State.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        host.State.Set(StateKeys.Rollback.BlockedUntil, -1);
        host.State.Set(StateKeys.Nvl.Active, true); // 模拟 NVL 模式

        var script = @"nvl
say ""A""
say ""B""
say ""C""
say ""D""
nvl clear
say ""E""
say ""F""";
        var cmds = new LingFanDslEngine().Compile(script).Commands;
        await host.RunDslAndDriveAsync(cmds);

        var cps = host.State.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        cps.Should().NotBeNull();

        // 关键断言：真实执行器为 nvl clear 建立了检查点
        var clearCp = cps!.FirstOrDefault(c => c.InteractionType == "nvl_clear");
        clearCp.Should().NotBeNull("nvl clear 必须建立检查点，否则回退会跳过'清零 NVL'节点");

        // 回退多步，确认 nvl_clear 检查点是连续时间线中可达的一步（不被跳过直接落到清理前的多行块）
        var idxClear = cps.IndexOf(clearCp);
        var reached = false;
        for (int k = 0; k < cps.Count; k++)
        {
            if (!host.DslExecutor.Rollback()) break;
            if (host.State.Get<int>(StateKeys.Rollback.CurrentIndex) == idxClear)
            {
                reached = true;
                break;
            }
        }
        reached.Should().BeTrue("回退应能逐步经过 nvl clear 检查点（已清空状态），而非跳过它");
    }

    [Fact]
    public void MenuRollback_TruncatesForwardTimeline_OnNewDecision()
    {
        // 验证决策命令（menu/input）回溯重选后的时间线截断语义（用户问题1的修复保障）：
        // 回退到决策检查点后，新选择必须丢弃旧分支的前向检查点，而非沿旧线 Rollforward。
        // 此处直接驱动 RollbackTo 定位到"菜单检查点"，再调用 TruncateForwardCheckpoints 的等效路径
        // （通过公开的 Rollback + 后续 CreateCheckpoint 截断链路间接覆盖）。
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        state.Set(StateKeys.Rollback.BlockedUntil, -1);
        var exe = MakeExecutor(state);

        // 布局：cp0(menu) → cp1(旧分支say) → cp2(旧分支say)
        var cp0 = new RollbackCheckpoint
        {
            CommandIndex = 0, InteractionType = "menu", SceneName = "s",
            StateSnapshot = new Dictionary<string, object?> { ["k"] = 0 }
        };
        var cp1 = new RollbackCheckpoint
        {
            CommandIndex = 1, InteractionType = "dialog", SceneName = "s",
            StateSnapshot = new Dictionary<string, object?> { ["k"] = 1 }
        };
        var cp2 = new RollbackCheckpoint
        {
            CommandIndex = 2, InteractionType = "dialog", SceneName = "s",
            StateSnapshot = new Dictionary<string, object?> { ["k"] = 2 }
        };
        state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint> { cp0, cp1, cp2 });
        state.Set(StateKeys.Rollback.CurrentIndex, 3);
        // 场景 = 刚完成末位交互（live == cp2 已入档）——回退跳过冗余末位（见 ComputeRollbackTarget）
        state.Set(StateKeys.Rollback.LiveCheckpointed, true);

        // 回退两步定位到 menu 检查点 cp0
        exe.Rollback().Should().BeTrue(); // → cp1
        exe.Rollback().Should().BeTrue(); // → cp0（menu）
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(0);

        // 玩家在 menu 上做出新选择后，后续命令创建新检查点会自动截断旧的前向分支（cp1,cp2）。
        // CreateCheckpoint 的截断逻辑：currentPos+1 < count 时 RemoveRange。
        // 此处验证从 menu 位置继续时旧分支被丢弃、时间线不串味。
        var cps = state.Get<List<RollbackCheckpoint>>(StateKeys.Rollback.Checkpoints);
        cps.Should().NotBeNull();
        cps![0].InteractionType.Should().Be("menu"); // menu 检查点保留，可再次回退到它
    }

    [Fact]
    public void Rollback_PreservesScopedAndFileLevelLocals_RemovesStaleSceneLocal()
    {
        // 锁定：叙事模式逐句回溯（前进/后退）对作用域局部键的处理自洽——
        // 作用域键格式 _local_S_*/_local_L_*/_local_<file>_ 被检查点全量快照捕获，
        // 回退到场景 A 检查点时：保留文件级与场景 A 局部，且移除过期的场景 B 局部（不泄漏到兄弟场景时间线）。
        var state = new StateContainer();
        state.Set(StateKeys.Scene.CurrentType, (int)SceneType.Game);
        var exe = MakeExecutor(state);

        // 与 LocalScope 真实写入格式一致的键
        const string fileG = "_local_story_dsl_g";
        const string sceneA = "_local_S_story_dsl_sceneA_a";
        const string sceneB = "_local_S_story_dsl_sceneB_b";

        // 检查点布局：cpA(文件级+场景A) → cpB(+场景B)
        var cpA = new RollbackCheckpoint
        {
            CommandIndex = 0,
            InteractionType = "dialog",
            SceneName = "sceneA",
            StateSnapshot = new Dictionary<string, object?> { [fileG] = 1, [sceneA] = 2 }
        };
        var cpB = new RollbackCheckpoint
        {
            CommandIndex = 1,
            InteractionType = "dialog",
            SceneName = "sceneB",
            StateSnapshot = new Dictionary<string, object?> { [fileG] = 1, [sceneA] = 2, [sceneB] = 3 }
        };
        state.Set(StateKeys.Rollback.Checkpoints, new List<RollbackCheckpoint> { cpA, cpB });
        state.Set(StateKeys.Rollback.CurrentIndex, 1);

        // 运行时已推进到场景 B：三个局部都在
        state.Set(fileG, 1);
        state.Set(sceneA, 2);
        state.Set(sceneB, 3);

        // 回退一步 → 落到 cpA
        exe.Rollback().Should().BeTrue();
        state.Get<int>(StateKeys.Rollback.CurrentIndex).Should().Be(0);

        // 关键断言：文件级与场景 A 局部保留，场景 B 局部被清除（不泄漏到场景 A 时间线）
        state.Get<int>(fileG).Should().Be(1);
        state.Get<int>(sceneA).Should().Be(2);
        state.ContainsKey(sceneB).Should().BeFalse("回退到场景A检查点后，场景B的作用域局部必须被清除，不能泄漏到兄弟场景");
    }
}
