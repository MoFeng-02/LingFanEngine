using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// AudioHandlers 集成测试：BGM / SE / 语音 / 环境音 的播放与停止。
/// 通过注入 FakeAudioManager 断言调用，并验证带状态键的 handler 写入状态。
/// </summary>
public class AudioHandlersTests
{
    [Fact]
    public void PlayBgmHandler_SetsStateAndCallsManager()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new PlayBgmHandler().Handle(new PlayBgmCommand { Path = "bgm.ogg", Volume = 0.7f, AutoStop = true }, ctx);
        ctx.State.Get<string>(StateKeys.Audio.CurrentBgmPath).Should().Be("bgm.ogg");
        ctx.State.Get<float>(StateKeys.Audio.CurrentBgmVolume).Should().Be(0.7f);
        ctx.State.Get<bool?>(StateKeys.Audio.BgmAutoStop).Should().BeTrue();
        mgr.LastBgmPath.Should().Be("bgm.ogg");
        mgr.PlayBgmCount.Should().Be(1);
    }

    [Fact]
    public void PlaySeHandler_CallsManager()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new PlaySeHandler().Handle(new PlaySeCommand { Path = "click.wav", Volume = 0.9f }, ctx);
        mgr.LastSePath.Should().Be("click.wav");
        mgr.PlaySeCount.Should().Be(1);
    }

    [Fact]
    public void PlayVoiceHandler_SetsAutoStopAndCallsManager()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new PlayVoiceHandler().Handle(new PlayVoiceCommand { Path = "v.wav", Volume = 1.0f, AutoStop = false }, ctx);
        ctx.State.Get<bool?>(StateKeys.Audio.VoiceAutoStop).Should().BeFalse();
        mgr.LastVoicePath.Should().Be("v.wav");
        mgr.PlayVoiceCount.Should().Be(1);
    }

    [Fact]
    public void BgmQueueHandler_CallsManager()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new BgmQueueHandler().Handle(new BgmQueueCommand { Path = "q.ogg", Volume = 0.5f, CrossFadeDuration = 2.0 }, ctx);
        mgr.LastQueuedBgmPath.Should().Be("q.ogg");
    }

    [Fact]
    public void PlayAmbientHandler_CallsManager()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new PlayAmbientHandler().Handle(new PlayAmbientCommand { Path = "rain.ogg", Loop = true, Volume = 0.6f }, ctx);
        mgr.LastAmbientPath.Should().Be("rain.ogg");
        mgr.LastAmbientLoop.Should().BeTrue();
    }

    [Fact]
    public void StopAmbientHandler_And_StopVoiceHandler_CallManager()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new StopAmbientHandler().Handle(new StopAmbientCommand(), ctx);
        new StopVoiceHandler().Handle(new StopVoiceCommand(), ctx);
        mgr.StopAmbientCalled.Should().BeTrue();
        mgr.StopVoiceCalled.Should().BeTrue();
    }

    [Fact]
    public void AudioHandlers_NullManager_DoNotThrow()
    {
        var ctx = new FakeCommandContext(); // AudioManager 默认 null
        var act = () =>
        {
            new PlayBgmHandler().Handle(new PlayBgmCommand { Path = "x" }, ctx);
            new PlaySeHandler().Handle(new PlaySeCommand { Path = "x" }, ctx);
            new PlayVoiceHandler().Handle(new PlayVoiceCommand { Path = "x" }, ctx);
            new BgmQueueHandler().Handle(new BgmQueueCommand { Path = "x" }, ctx);
            new PlayAmbientHandler().Handle(new PlayAmbientCommand { Path = "x" }, ctx);
            new StopAmbientHandler().Handle(new StopAmbientCommand(), ctx);
            new StopVoiceHandler().Handle(new StopVoiceCommand(), ctx);
        };
        act.Should().NotThrow();
    }
}
