using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Services.Core;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// 视频播放器与 VideoPresenter 单元测试（Tier A 无宿主）。
/// <para>覆盖：</para>
/// <para>1. NullVideoPlayer.Control 返回 null</para>
/// <para>2. VideoPresenter + null Control 优雅降级（不崩溃，路径仍被追踪）</para>
/// <para>3. VideoPresenter + 非 GpuMediaPlayer Control 优雅跳过</para>
/// <para>4. VideoPresenter.Detach 清理不崩溃</para>
/// </summary>
public class VideoPlayerTests
{
    // ───────────────────────── NullVideoPlayer ─────────────────────────

    [Fact]
    public void NullVideoPlayer_Control_ReturnsNull()
    {
        var player = new NullVideoPlayer();
        player.Control.Should().BeNull();
    }

    [Fact]
    public void NullVideoPlayer_Control_AlwaysReturnsNull()
    {
        var player = new NullVideoPlayer();
        // 多次访问始终返回 null
        player.Control.Should().BeNull();
        player.Control.Should().BeNull();
    }

    // ────────────────────── VideoPresenter + null Control ──────────────────────

    /// <summary>
    /// 最小假 IVideoPlayer——Control 可控返回 null 或任意对象。
    /// </summary>
    private sealed class FakeVideoPlayer : IVideoPlayer
    {
        public object? Control { get; set; }
    }

    [Fact]
    public void VideoPresenter_Update_NoVideoPath_DoesNotThrow()
    {
        var state = new StateContainer();
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = null });

        var act = () => vp.Update();
        act.Should().NotThrow();
    }

    [Fact]
    public void VideoPresenter_Update_NullControl_VideoPathSet_DoesNotThrow()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Video.CurrentPath, "test.mp4");
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = null });

        var act = () => vp.Update();
        act.Should().NotThrow();
    }

    [Fact]
    public void VideoPresenter_Update_NullControl_VideoPathChange_TriggersNoCrash()
    {
        var state = new StateContainer();
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = null });

        // 首次设路径
        state.Set(StateKeys.Video.CurrentPath, "a.mp4");
        vp.Update();

        // 切换路径
        state.Set(StateKeys.Video.CurrentPath, "b.mp4");
        var act = () => vp.Update();
        act.Should().NotThrow();
    }

    [Fact]
    public void VideoPresenter_Update_NullControl_CutsceneActive_DoesNotThrow()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Video.CutsceneActive, true);
        state.Set(StateKeys.Video.CutsceneSkipable, true);
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = null });

        var act = () => vp.Update();
        act.Should().NotThrow();
    }

    // ────────────────── VideoPresenter + foreign Control ──────────────────

    [Fact]
    public void VideoPresenter_Update_ForeignControl_DoesNotThrow()
    {
        // Control 返回一个非 GpuMediaPlayer 的对象——VideoPresenter 应通过 is 模式匹配跳过
        var state = new StateContainer();
        state.Set(StateKeys.Video.CurrentPath, "test.mp4");
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = new object() });

        var act = () => vp.Update();
        act.Should().NotThrow();
    }

    // ────────────────────── VideoPresenter.Detach ──────────────────────

    [Fact]
    public void VideoPresenter_Detach_WithoutAttach_DoesNotThrow()
    {
        var state = new StateContainer();
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = null });

        var act = () => vp.Detach();
        act.Should().NotThrow();
    }

    [Fact]
    public void VideoPresenter_Detach_AfterAttachWithNullPanels_DoesNotThrow()
    {
        var state = new StateContainer();
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = null });
        vp.Attach(null, null);

        var act = () => vp.Detach();
        act.Should().NotThrow();
    }

    [Fact]
    public void VideoPresenter_Detach_AfterVideoUpdate_DoesNotThrow()
    {
        var state = new StateContainer();
        state.Set(StateKeys.Video.CurrentPath, "test.mp4");
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = null });
        vp.Attach(null, null);
        vp.Update();

        var act = () => vp.Detach();
        act.Should().NotThrow();
    }

    // ────────────────────── VideoPresenter.Attach/Detach 循环 ──────────────────────

    [Fact]
    public void VideoPresenter_Reattach_DoesNotThrow()
    {
        var state = new StateContainer();
        var vp = new VideoPresenter(state, new FakeVideoPlayer { Control = null });

        vp.Attach(null, null);
        vp.Update();
        vp.Detach();

        // 再次 Attach + Update
        vp.Attach(null, null);
        var act = () => vp.Update();
        act.Should().NotThrow();
    }
}
