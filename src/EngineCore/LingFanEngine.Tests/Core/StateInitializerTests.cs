using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.Core;

public class StateInitializerTests
{
    [Fact]
    public void Initialize_SetsAllDefaultKeys()
    {
        var state = new StateContainer();
        new StateInitializer().Initialize(state);

        state.ContainsKey(StateKeys.History.Entries).Should().BeTrue();
        state.Get<int>(StateKeys.History.MaxCount).Should().Be(100);
        state.Get<bool>(StateKeys.Playback.SkipActive).Should().BeFalse();
        state.Get<float>(StateKeys.Preferences.MasterVolume).Should().Be(1.0f);
        state.Get<float>(StateKeys.Preferences.BgmVolume).Should().Be(0.8f);
        state.Get<bool>(StateKeys.Dialog.TypewriterEnabled).Should().BeTrue();
        state.Get<bool>(StateKeys.Video.AutoPlay).Should().BeTrue();
        state.Get<int>(StateKeys.Scene.CurrentType).Should().Be((int)SceneType.Game);
        state.ContainsKey(StateKeys.Debug.Logs).Should().BeTrue();
    }

    [Fact]
    public void Initialize_IsIdempotent_DoesNotOverwriteExisting()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Playback.SkipActive, true);
        new StateInitializer().Initialize(state);

        // 已存在的值被保留
        state.Get<bool>(StateKeys.Playback.SkipActive).Should().BeTrue();
        // 其余键仍是默认值
        state.Get<int>(StateKeys.History.MaxCount).Should().Be(100);
        state.Get<float>(StateKeys.Preferences.MasterVolume).Should().Be(1.0f);
    }

    [Fact]
    public void Initialize_CanBeCalledTwice_Safely()
    {
        var state = new StateContainer();
        new StateInitializer().Initialize(state);
        var before = state.Get<int>(StateKeys.History.MaxCount);
        new StateInitializer().Initialize(state);
        state.Get<int>(StateKeys.History.MaxCount).Should().Be(before);
    }
}
