using System.Linq;
using System.Threading;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>
/// let/local 作用域端到端集成测试：编译期 SourceFile 注入 + 运行时 DslExecutor + LocalScope 路由。
/// <list type="bullet">
///   <item>编译期：filePath 注入到 IFileScopedCommand.SourceFile（跨文件隔离的基石）；</item>
///   <item>运行时：label 包裹的 let 各自独立键；内层读取回退到外层（文件级可见）；</item>
///   <item>跨文件：同名 let 落在不同作用域键，互不污染。</item>
/// </list>
/// </summary>
public class DslLocalScopeIntegrationTests
{
    [Fact]
    public void Compile_InjectsSourceFile_ForCrossFileIsolation()
    {
        var engine = new LingFanDslEngine();
        var result = engine.Compile("let \"x\" 1", "chapter1/intro.dsl");
        var cmd = result.Commands.OfType<SetVariableCommand>().First(c => c.Key == "_local_x");
        cmd.SourceFile.Should().Be("chapter1/intro.dsl");

        // 无 filePath 时不注入
        var noFile = engine.Compile("let \"x\" 1");
        noFile.Commands.OfType<SetVariableCommand>().First(c => c.Key == "_local_x")
            .SourceFile.Should().BeNull();
    }

    [Fact]
    public async Task Runtime_LabelScopedLet_AreIndependent_InnerSeesOuter()
    {
        var host = new EngineTestHost();
        var engine = new LingFanDslEngine();
        var script = """
            let "x" 0
            let "fileOnly" 99
            label sceneA:
            let "x" 1
            set "probeA" {x}
            set "probeFallback" {fileOnly}
            label sceneB:
            let "x" 2
            set "probeB" {x}
            """;
        var result = engine.Compile(script, "story.dsl");

        // 阶段一：从 index 0 跑序言 + sceneA。InsertEndSentinels 在 sceneA 体末尾插入了 EndCommand
        // 哨兵，线性执行到该哨兵即 return（引擎语义：label 是场景/子例程入口，需跳转进入，
        // 不应从 index 0 线性贯穿所有 label），因此 sceneB 体本阶段不会执行。
        host.DslExecutor.LoadCommands(result.Commands, result.Labels);
        host.DslExecutor.Start();
        await DriveUntilIdleAsync(host);

        var s = host.State;
        // 文件顶层作用域
        s.Get<object>("_local_story_dsl_x").Should().Be(0);
        s.Get<object>("_local_story_dsl_fileOnly").Should().Be(99);
        // 场景级作用域（label=sceneA）
        s.Get<object>("_local_story_dsl_sceneA_x").Should().Be(1);
        // 内层读取：sceneA 内 x=1；fileOnly 回退到文件级 99（内层可见外层）
        s.Get<object>("probeA").Should().Be(1);
        s.Get<object>("probeFallback").Should().Be(99);

        // 阶段二：单独跳转进 sceneB 跑其体，验证兄弟场景不互相污染
        host.DslExecutor.StartFromLabel("sceneB");
        await DriveUntilIdleAsync(host);

        s.Get<object>("_local_story_dsl_sceneB_x").Should().Be(2);
        s.Get<object>("probeB").Should().Be(2);
        // sceneA / 文件级的值未被 sceneB 覆盖
        s.Get<object>("_local_story_dsl_sceneA_x").Should().Be(1);
        s.Get<object>("_local_story_dsl_x").Should().Be(0);
    }

    /// <summary>
    /// 轮询 DslExecutor.IsRunning，直到主循环因 EndCommand/命令耗尽而退出（空闲），或超时。
    /// 用于驱动无交互阻塞命令的脚本段（let/set 等同步命令无需发完成信号）。
    /// </summary>
    private static async Task DriveUntilIdleAsync(EngineTestHost host, int timeoutMs = 8000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (host.DslExecutor.IsRunning && !cts.IsCancellationRequested)
                await Task.Delay(5, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 超时——交由调用方对最终状态做断言
        }
    }

    [Fact]
    public async Task Runtime_CrossFile_SameLocalName_Independent()
    {
        var host = new EngineTestHost();
        var engine = new LingFanDslEngine();

        var a = engine.Compile("let \"x\" 1", "A.dsl");
        var b = engine.Compile("let \"x\" 2", "B.dsl");

        await host.RunDslAndDriveAsync(a.Commands, a.Labels);
        await host.RunDslAndDriveAsync(b.Commands, b.Labels);

        var s = host.State;
        s.Get<object>("_local_A_dsl_x").Should().Be(1);
        s.Get<object>("_local_B_dsl_x").Should().Be(2);
        // 互不妨碍
        s.Get<object>("_local_B_dsl_x").Should().NotBe(s.Get<object>("_local_A_dsl_x"));
    }

    [Fact]
    public async Task Runtime_SceneEqualsLabel_NormalizedToSingleScopeSegment()
    {
        // 预置 __current_scene_name 模拟进入场景（等价于 scene 命令写入），
        // 验证 ApplyCommandScope 在「最近前置 label == 场景名」时归一化为场景级作用域，
        // 而非产生重复键 _local_<file>_<scene>_<scene>_x。
        var host = new EngineTestHost();
        host.State.Set(StateKeys.Scene.CurrentName, "sceneA");
        var engine = new LingFanDslEngine();
        var script = """
            label sceneA:
            let "x" 1
            """;
        var result = engine.Compile(script, "story.dsl");
        await host.RunDslAndDriveAsync(result.Commands, result.Labels);

        host.State.Get<object>("_local_story_dsl_sceneA_x").Should().Be(1);
        // 不应出现重复场景名的脏键
        host.State.ContainsKey("_local_story_dsl_sceneA_sceneA_x").Should().BeFalse();
    }
}
