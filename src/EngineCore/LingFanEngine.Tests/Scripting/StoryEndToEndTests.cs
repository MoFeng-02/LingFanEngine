using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Scripting;

/// <summary>
/// DSL 端到端冒烟（P0 集成缺口验证）。
/// <para>从真实 .story 文件经 StoryLoader 加载并编译，再把编译出的命令序列喂给真实 DslExecutor 跑通，
/// 验证「文件 → 解析/编译 → 执行」链路对真实脚本可用（单元绿 ≠ 真实脚本能跑）。</para>
/// <para>区别于 DslExecutorTests（手工 ICommand 序列）与 StoryLoaderTests（只验解析不验执行）：本测试覆盖
/// 真实文件经真实加载管线编译后、由真实 DslExecutor 完整执行的桥接链路。</para>
/// </summary>
public class StoryEndToEndTests : IDisposable
{
    private readonly string _tempDir;

    public StoryEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"lf_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task RealStoryFile_DrivesSaySetIfBranch_ToCompletion()
    {
        // 真实 .story 文件（纯 DSL flow script：label / set / if / say），
        // 语法与引擎发布的章节脚本一致（见 Demo Stories/chapter1_intro.story）。
        var file = Path.Combine(_tempDir, "e2e_zh-CN.story");
        await File.WriteAllTextAsync(file, """
            label start:
              set "gold" {100}
              if {gold >= 50}
                set "rich" {1}
                say "你很有钱"
              end
              say "结束"
            """);

        // 1) 从真实文件加载 + 编译（StoryLoader 真实管线）
        var loader = new StoryLoader(new LingFanDslEngine(), new FakeCommandPipeline(), new StateContainer(), new FakeSceneRegistry());
        var story = await loader.LoadFromFileAsync(file);
        story.Should().NotBeNull();
        story!.CompiledCommands.Should().NotBeNull().And.NotBeEmpty();
        story.Labels.Should().NotBeNull().And.ContainKey("start");

        // 2) 真实 DSL 命令序列经真实 DslExecutor 跑通（集成缺口验证）
        var host = new EngineTestHost();
        await host.RunDslAndDriveAsync(story.CompiledCommands!, story.Labels);

        host.DslExecutor.IsRunning.Should().BeFalse();
        host.State.Get<int>("gold").Should().Be(100);   // set 变量生效
        host.State.Get<int>("rich").Should().Be(1);     // if 真分支已执行
    }
}
