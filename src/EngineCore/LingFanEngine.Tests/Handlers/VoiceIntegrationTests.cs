using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Core.Handlers;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Handlers;

/// <summary>
/// Voice 播放集成测试（盲区验证 T11）。
/// <para>headless 无音频设备，验证 DSL 语音流经 handler 到媒体桩(IAudioManager)的正确路由与时序，
/// 不验证真实声卡输出（那是 LibVlc 媒体层的职责，由真实设备端到端保证）。</para>
/// <para>覆盖：say voice= 行内语音、独立 voice= 语句、stop_voice、单轨原子替换（每句播各自语音，
/// 替换由媒体层完成、handler 不在换句时多发 StopVoice）。</para>
/// </summary>
public class VoiceIntegrationTests
{
    [Fact]
    public void ShowDialog_WithVoice_PlaysVoiceAndSetsAutoStop()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new ShowDialogHandler().Handle(new ShowDialogCommand { Text = "你好", VoicePath = "voice/hi.wav" }, ctx);

        mgr.LastVoicePath.Should().Be("voice/hi.wav");
        mgr.PlayVoiceCount.Should().Be(1);
        ctx.State.Get<bool?>(StateKeys.Audio.VoiceAutoStop).Should().BeTrue();
    }

    [Fact]
    public void ShowDialog_WithoutVoice_DoesNotPlay()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new ShowDialogHandler().Handle(new ShowDialogCommand { Text = "你好" }, ctx);

        mgr.PlayVoiceCount.Should().Be(0);
    }

    [Fact]
    public void VoiceStmt_PlaysVoice()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new PlayVoiceHandler().Handle(new PlayVoiceCommand { Path = "voice/x.wav", AutoStop = false }, ctx);

        mgr.LastVoicePath.Should().Be("voice/x.wav");
        mgr.PlayVoiceCount.Should().Be(1);
        ctx.State.Get<bool?>(StateKeys.Audio.VoiceAutoStop).Should().BeFalse();
    }

    [Fact]
    public void StopVoice_StopsVoice()
    {
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };
        new StopVoiceHandler().Handle(new StopVoiceCommand(), ctx);

        mgr.StopVoiceCalled.Should().BeTrue();
    }

    [Fact]
    public void SaySequence_AtomicVoiceReplacement_NoSpuriousStop()
    {
        // 单轨原子替换：每句 say 播放各自语音；换句时媒体层负责替换当前轨，
        // handler 不应多发 StopVoice（否则会与媒体层替换逻辑重复/冲突）。
        var mgr = new FakeAudioManager();
        var ctx = new FakeCommandContext { AudioManager = mgr };

        new ShowDialogHandler().Handle(new ShowDialogCommand { Text = "a", VoicePath = "v1.wav" }, ctx);
        new ShowDialogHandler().Handle(new ShowDialogCommand { Text = "b", VoicePath = "v2.wav" }, ctx);

        mgr.PlayVoiceCount.Should().Be(2);
        mgr.LastVoicePath.Should().Be("v2.wav");
        mgr.StopVoiceCalled.Should().BeFalse();
    }
}
