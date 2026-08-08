using System;
using FluentAssertions;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Views;
using Xunit;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// 视频空实现单元测试（Tier A 无宿主）。
/// <para>桌面视频后端暂缓实现期间，全平台视频逻辑面为 NullVideoPlayer + NullVideoPresenter。</para>
/// <para>覆盖：</para>
/// <para>1. NullVideoPlayer 全部逻辑面方法为空操作、状态恒 Stopped</para>
/// <para>2. NullVideoPresenter Attach/Detach/Update 任意顺序调用不崩溃</para>
/// </summary>
public class VideoPlayerTests
{
    // ───────────────────────── NullVideoPlayer ─────────────────────────

    [Fact]
    public void NullVideoPlayer_State_IsAlwaysStopped()
    {
        var player = new NullVideoPlayer();
        player.GetStateAsync().AsTask().Result.Should().Be(PlaybackState.Stopped);
        player.GetPositionAsync().AsTask().Result.Should().Be(TimeSpan.Zero);
        player.GetDurationAsync().AsTask().Result.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void NullVideoPlayer_AllMethods_NoThrow()
    {
        var player = new NullVideoPlayer();
        var act = () =>
        {
            player.LoadAsync("x.mp4").GetAwaiter().GetResult();
            player.SetVolumeAsync(0).GetAwaiter().GetResult();
            player.SetMuteAsync(true).GetAwaiter().GetResult();
            player.PlayAsync().GetAwaiter().GetResult();
            player.PauseAsync().GetAwaiter().GetResult();
            player.ResumeAsync().GetAwaiter().GetResult();
            player.SeekAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            player.StopAsync().GetAwaiter().GetResult();
            player.Activate();
            player.Deactivate();
            player.SetZIndex(0);
            player.SetBounds(0, 0, 1, 1);
            player.DisposeAsync().AsTask().GetAwaiter().GetResult();
        };
        act.Should().NotThrow();
    }

    // ───────────────────────── NullVideoPresenter ─────────────────────────

    [Fact]
    public void NullVideoPresenter_LifecycleAnyOrder_NoThrow()
    {
        IVideoPresenter presenter = new NullVideoPresenter();
        var act = () =>
        {
            presenter.Update();               // 未 Attach 直接 Update
            presenter.Detach();               // 未 Attach 直接 Detach
            presenter.Attach(null, null);     // 空面板 Attach
            presenter.Update();
            presenter.Detach();
            presenter.Attach(null, null);     // Reattach
            presenter.Update();
        };
        act.Should().NotThrow();
    }
}
