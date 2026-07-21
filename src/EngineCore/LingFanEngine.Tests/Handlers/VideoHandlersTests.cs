using FluentAssertions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// VideoHandlers 集成测试：播放 / 停止 / 暂停 / 恢复 / 跳转 / 过场。
/// 通过注入 FakeVideoManager 断言调用。
/// </summary>
public class VideoHandlersTests
{
    [Fact]
    public void PlayVideoHandler_CallsManagerWithArgs()
    {
        var mgr = new FakeVideoManager();
        var ctx = new FakeCommandContext { VideoManager = mgr };
        new PlayVideoHandler().Handle(new PlayVideoCommand { Path = "c.mp4", Volume = 0.8f, Loop = true, AutoPlay = false }, ctx);
        mgr.LastPlayPath.Should().Be("c.mp4");
        mgr.LastPlayVolume.Should().Be(0.8f);
        mgr.LastPlayLoop.Should().BeTrue();
        mgr.LastPlayAutoPlay.Should().BeFalse();
    }

    [Fact]
    public void StopPauseResumeHandler_CallManager()
    {
        var mgr = new FakeVideoManager();
        var ctx = new FakeCommandContext { VideoManager = mgr };
        new StopVideoHandler().Handle(new StopVideoCommand(), ctx);
        new PauseVideoHandler().Handle(new PauseVideoCommand(), ctx);
        new ResumeVideoHandler().Handle(new ResumeVideoCommand(), ctx);
        mgr.StopCalled.Should().BeTrue();
        mgr.PauseCalled.Should().BeTrue();
        mgr.ResumeCalled.Should().BeTrue();
    }

    [Fact]
    public void SeekVideoHandler_CallsManagerWithPosition()
    {
        var mgr = new FakeVideoManager();
        var ctx = new FakeCommandContext { VideoManager = mgr };
        new SeekVideoHandler().Handle(new SeekVideoCommand { Position = 12.5 }, ctx);
        mgr.LastSeek.Should().Be(System.TimeSpan.FromSeconds(12.5));
    }

    [Fact]
    public void CutsceneHandler_CallsManager()
    {
        var mgr = new FakeVideoManager();
        var ctx = new FakeCommandContext { VideoManager = mgr };
        new CutsceneHandler().Handle(new CutsceneCommand { Path = "cut.mp4", Skipable = false, Volume = 0.5f }, ctx);
        mgr.LastCutscenePath.Should().Be("cut.mp4");
        mgr.LastCutsceneSkipable.Should().BeFalse();
        mgr.LastCutsceneVolume.Should().Be(0.5f);
    }

    [Fact]
    public void VideoHandlers_NullManager_DoNotThrow()
    {
        var ctx = new FakeCommandContext(); // VideoManager 默认 null
        var act = () =>
        {
            new PlayVideoHandler().Handle(new PlayVideoCommand { Path = "x" }, ctx);
            new StopVideoHandler().Handle(new StopVideoCommand(), ctx);
            new PauseVideoHandler().Handle(new PauseVideoCommand(), ctx);
            new ResumeVideoHandler().Handle(new ResumeVideoCommand(), ctx);
            new SeekVideoHandler().Handle(new SeekVideoCommand { Position = 1 }, ctx);
            new CutsceneHandler().Handle(new CutsceneCommand { Path = "x" }, ctx);
        };
        act.Should().NotThrow();
    }
}
