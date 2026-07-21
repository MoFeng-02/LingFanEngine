using System.Threading.Tasks;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Games;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Games;

/// <summary>
/// StoryScript 端到端烟测（Phase 3 闭环验证）。
/// 继承 StoryScript 写最小剧情，验证 Ctrl.SayAsync 经 GameController 发命令 + 等待闭环可跑通。
/// </summary>
public class StoryScriptTests
{
    private sealed class SayStory : StoryScript
    {
        public override string SceneName => "say_scene";
        public string? Marker;

        public override async Task RunAsync()
        {
            await SayAsync("你好，世界");
            Marker = "reached_end";
        }
    }

    [Fact]
    public async Task RunAsync_DrivesSayAsyncToCompletion()
    {
        var host = new EngineTestHost();
        var story = new SayStory();
        story.Initialize(host.GameController, host.State, host.Pipeline, new FakeSceneRegistry());

        var runTask = story.RunAsync();
        await Task.Yield();
        host.AdvanceDialog();                     // 推进唯一的对话
        await runTask.WaitAsync(System.TimeSpan.FromSeconds(5));

        story.Marker.Should().Be("reached_end");
    }

    [Fact]
    public async Task RunAsync_CanSequenceMultipleSays()
    {
        var host = new EngineTestHost();
        var story = new MultiSayStory();
        story.Initialize(host.GameController, host.State, host.Pipeline, new FakeSceneRegistry());

        var runTask = story.RunAsync();
        // 多句 SayAsync 共享 WaitingSayComplete 键，必须用持续轮询自动推进，避免被置 false 间隙吞掉。
        var driver = host.AutoAdvanceDialogAsync(() => runTask.IsCompleted);
        await runTask.WaitAsync(System.TimeSpan.FromSeconds(5));
        await driver;

        story.Count.Should().Be(3);
    }

    private sealed class MultiSayStory : StoryScript
    {
        public override string SceneName => "multi_say";
        public int Count;

        public override async Task RunAsync()
        {
            await SayAsync("一");
            Count++;
            await SayAsync("二");
            Count++;
            await SayAsync("三");
            Count++;
        }
    }
}
