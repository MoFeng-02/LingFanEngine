using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// PreferencesService 偏好设置服务测试
/// <para>偏好值存储在 IStateContainer 中（__pref_* 前缀），测试音量/文字速度/全屏/语言/有效音量。</para>
/// </summary>
public class PreferencesServiceTests
{
    private readonly StateContainer _state = new();
    private readonly PreferencesService _service;

    public PreferencesServiceTests()
    {
        _service = new PreferencesService(_state);
    }

    // ========== 默认值 ==========

    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        _service.MasterVolume.Should().Be(1.0f);
        _service.BgmVolume.Should().Be(0.8f);
        _service.SeVolume.Should().Be(0.6f);
        _service.VoiceVolume.Should().Be(1.0f);
        _service.MasterMuted.Should().BeFalse();
        _service.TextSpeed.Should().Be(30.0);
        _service.AutoForwardDelay.Should().Be(3.0);
        _service.SkipUnseen.Should().BeFalse();
        _service.Fullscreen.Should().BeFalse();
    }

    // ========== 音量夹持 ==========

    [Fact]
    public void MasterVolume_ClampsToRange()
    {
        _service.MasterVolume = 2.0f;
        _service.MasterVolume.Should().Be(1.0f);

        _service.MasterVolume = -0.5f;
        _service.MasterVolume.Should().Be(0.0f);
    }

    [Fact]
    public void BgmVolume_ClampsToRange()
    {
        _service.BgmVolume = 5.0f;
        _service.BgmVolume.Should().Be(1.0f);
    }

    [Fact]
    public void SeVolume_ClampsToRange()
    {
        _service.SeVolume = -1.0f;
        _service.SeVolume.Should().Be(0.0f);
    }

    [Fact]
    public void VoiceVolume_ClampsToRange()
    {
        _service.VoiceVolume = 10.0f;
        _service.VoiceVolume.Should().Be(1.0f);
    }

    // ========== 有效音量 ==========

    [Fact]
    public void GetEffectiveVolume_Muted_ReturnsZero()
    {
        _service.MasterVolume = 0.5f;
        _service.MasterMuted = true;
        _service.GetEffectiveVolume("bgm").Should().Be(0.0f);
    }

    [Fact]
    public void GetEffectiveVolume_Bgm_MultipliesByMaster()
    {
        _service.MasterVolume = 0.5f;
        _service.BgmVolume = 1.0f;
        _service.GetEffectiveVolume("bgm").Should().Be(0.5f);
    }

    [Fact]
    public void GetEffectiveVolume_Se_MultipliesByMaster()
    {
        _service.MasterVolume = 0.5f;
        _service.SeVolume = 1.0f;
        _service.GetEffectiveVolume("se").Should().Be(0.5f);
    }

    [Fact]
    public void GetEffectiveVolume_Voice_MultipliesByMaster()
    {
        _service.MasterVolume = 0.25f;
        _service.VoiceVolume = 1.0f;
        _service.GetEffectiveVolume("voice").Should().Be(0.25f);
    }

    [Fact]
    public void GetEffectiveVolume_UnknownChannel_ReturnsMaster()
    {
        _service.MasterVolume = 0.7f;
        _service.GetEffectiveVolume("other").Should().Be(0.7f);
    }

    // ========== 文字速度 ==========

    [Fact]
    public void TextSpeed_ClampsToMinimumOne()
    {
        _service.TextSpeed = 0.5;
        _service.TextSpeed.Should().Be(1.0);
    }

    // ========== 自动延迟同步 ==========

    [Fact]
    public void AutoForwardDelay_SyncsToPlaybackAutoDelay()
    {
        _service.AutoForwardDelay = 5.0;
        _state.Get<double>(StateKeys.Playback.AutoDelay).Should().Be(5.0);
    }

    // ========== 显示 / 语言 ==========

    [Fact]
    public void Fullscreen_Set_PersistsToState()
    {
        _service.Fullscreen = true;
        _service.Fullscreen.Should().BeTrue();
        _state.Get<bool>(StateKeys.Preferences.Fullscreen).Should().BeTrue();
    }

    [Fact]
    public void Language_Set_PersistsToState()
    {
        _service.Language = "en-US";
        _service.Language.Should().Be("en-US");
        _state.Get<string>(StateKeys.Preferences.Language).Should().Be("en-US");
    }

    [Fact]
    public void SkipUnseen_Set_PersistsToState()
    {
        _service.SkipUnseen = true;
        _service.SkipUnseen.Should().BeTrue();
    }
}
