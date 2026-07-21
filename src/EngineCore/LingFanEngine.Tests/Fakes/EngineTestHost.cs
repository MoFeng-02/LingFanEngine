using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;

namespace LingFanEngine.Tests.Fakes;

/// <summary>
/// Phase 3 集成测试宿主桩。
/// <para>
/// 直接用真实 <see cref="StateContainer"/> + 真实 <see cref="AsyncWaitService"/>(共享同一 state)
/// + 真实(或 fake) <see cref="CommandPipeline"/>，手工 new 四个重组件，避免整锅 DI 拉入
/// Avalonia 调度器与 LibVLC。
/// </para>
/// <para>
/// 等待机制是「谓词 + StateContainer.ValueChanged 事件驱动」：组件调用 IAsyncWaitService.WaitForAsync
/// 等待某个状态键；测试里对正确的状态键调用 AdvanceXxx / CompleteXxx 即可精确解除阻塞，不会挂死。
/// 每个阻塞调用都包了超时兜底(<see cref="LingFanEngineOptions.BlockingTimeoutSeconds"/>)，
/// 但确定性测试应主动 Set 对应键来精确推进。
/// </para>
/// </summary>
public sealed class EngineTestHost : IDisposable
{
    /// <summary>真实状态容器（SSOT），AsyncWaitService 订阅其 ValueChanged 事件。</summary>
    public StateContainer State { get; }

    /// <summary>真实等待服务，构造时订阅 State.ValueChanged。</summary>
    public AsyncWaitService Wait { get; }

    /// <summary>命令管道。默认真实无界 CommandPipeline（SendAsync 永不阻塞）；可传 FakeCommandPipeline 断言发出的命令。</summary>
    public ICommandPipeline Pipeline { get; }

    /// <summary>引擎选项。默认 EnableTimeSystem=false（叙事模式）。</summary>
    public LingFanEngineOptions Options { get; }

    /// <summary>富阻塞交互控制器（SayAsync/ShowMenuAsync/InputAsync/TransitionAsync…）。</summary>
    public GameController GameController { get; }

    /// <summary>DSL 执行器（LoadCommands + Start 后 fire-and-forget 跑主循环）。</summary>
    public DslExecutor DslExecutor { get; }

    /// <summary>若使用 FakeCommandPipeline，可通过此处读取被发出的命令列表；否则为 null。</summary>
    public FakeCommandPipeline? FakePipeline { get; }

    public EngineTestHost(bool enableTimeSystem = false, ICommandPipeline? pipeline = null)
    {
        State = new StateContainer();
        Wait = new AsyncWaitService(State);
        Pipeline = pipeline ?? new CommandPipeline();
        FakePipeline = Pipeline as FakeCommandPipeline;
        Options = new LingFanEngineOptions { EnableTimeSystem = enableTimeSystem };
        GameController = new GameController(Pipeline, State, Options, Wait);
        DslExecutor = new DslExecutor(State, Pipeline, Options, Wait);
    }

    // ============ 解除阻塞辅助（基于谓词/状态键） ============

    /// <summary>推进对话：SayAsync / ExtendDialogAsync / WaitForClickAsync / SkipableWaitAsync 等待此键变 true。</summary>
    public void AdvanceDialog() => State.Set(StateKeys.Dialog.WaitingSayComplete, true);

    /// <summary>完成对话信号：DslExecutor 的 ShowDialogCommand 分支等待 Dialog.Complete 变 true。</summary>
    public void CompleteDialog() => State.Set(StateKeys.Dialog.Complete, true);

    /// <summary>菜单选择（索引）。</summary>
    public void SelectMenu(int index) => State.Set(StateKeys.Menu.Selected, index);

    /// <summary>提交输入结果。</summary>
    public void SetInput(string value) => State.Set(StateKeys.Input.Result, value);

    /// <summary>call_screen 返回结果。</summary>
    public void CompleteScreen(string result) => State.Set(StateKeys.Screen.Result, result);

    /// <summary>过渡完成：先置 Active=true（模拟激活），再置 false（结束）。</summary>
    public void CompleteTransition()
    {
        State.Set(StateKeys.Transition.Active, true);
        State.Set(StateKeys.Transition.Active, false);
    }

    /// <summary>过场动画完成：先激活，再结束。</summary>
    public void CompleteCutscene()
    {
        State.Set(StateKeys.Video.CutsceneActive, true);
        State.Set(StateKeys.Video.CutsceneActive, false);
    }

    /// <summary>在后台延迟后解除某关键阻塞，模拟 UI/玩家响应（避免与组件注册 waiter 产生竞态）。</summary>
    public Task AdvanceAfterDelayAsync(Func<Task> advance, int delayMs = 20)
        => Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            await advance();
        });

    /// <summary>
    /// 自动推进对话直到 <paramref name="isDone"/> 为 true 或超时。
    /// <para>
    /// 用于驱动含多句 SayAsync 的剧情/脚本：每隔 <paramref name="intervalMs"/> 把
    /// WaitingSayComplete 置 true，唤醒当前在等待的 SayAsync。连续轮询可避免「单次 advance 被
    /// 两句 SayAsync 之间的置 false 间隙吞掉」的竞态（已实测会挂死）。
    /// </para>
    /// </summary>
    public async Task AutoAdvanceDialogAsync(Func<bool> isDone, int intervalMs = 5, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (!isDone() && !cts.IsCancellationRequested)
            {
                State.Set(StateKeys.Dialog.WaitingSayComplete, true);
                await Task.Delay(intervalMs, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 超时或完成——交给调用方对目标任务做最终 WaitAsync 判定
        }
    }

    /// <summary>
    /// 加载 DSL 命令并启动 DslExecutor，同时用一个状态驱动循环自动推进所有交互阻塞，
    /// 直到脚本跑完（IsRunning=false）或超时。这是 DslExecutor 集成测试的核心驱动。
    /// <para>
    /// 驱动逻辑：每轮读取 __dsl_current_index，对当前命令按类型发出对应的「完成」信号
    /// （ShowDialogCommand→Dialog.Complete、MenuCommand→Menu.Selected、InputCommand→Input.Result、
    /// CallScreenCommand→Screen.Result、TransitionCommand→先激活再结束）。非阻塞命令（SetVariable/
    /// Branch/Jump/Wait）无需驱动。
    /// </para>
    /// </summary>
    public async Task RunDslAndDriveAsync(
        IReadOnlyList<ICommand> commands,
        IReadOnlyDictionary<string, int>? labels = null,
        int intervalMs = 5,
        int timeoutMs = 8000)
    {
        DslExecutor.LoadCommands(commands, labels);
        DslExecutor.Start();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (DslExecutor.IsRunning && !cts.IsCancellationRequested)
            {
                var idx = State.Get<int>(StateKeys.Dsl.CurrentIndex);
                if (idx >= 0 && idx < commands.Count)
                {
                    switch (commands[idx])
                    {
                        case ShowDialogCommand:
                            CompleteDialog();
                            break;
                        case MenuCommand:
                            SelectMenu(0);
                            break;
                        case InputCommand:
                            SetInput("__dsl_input__");
                            break;
                        case CallScreenCommand:
                            CompleteScreen("__dsl_screen__");
                            break;
                        case TransitionCommand:
                            // 两阶段等待：先激活（给 stage-1 注册 waiter 的时间），再结束
                            State.Set(StateKeys.Transition.Active, true);
                            await Task.Delay(20, cts.Token);
                            State.Set(StateKeys.Transition.Active, false);
                            break;
                        // WaitCommand / SetVariable / Branch / Jump 等：无需驱动
                    }
                }
                await Task.Delay(intervalMs, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 超时——交由调用方对最终状态做断言（通常会发现未达预期而失败）
        }
    }

    public void Dispose()
    {
        if (Pipeline is IDisposable d) d.Dispose();
    }
}
