using FluentAssertions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Services.Core;
using Xunit;

namespace LingFanEngine.Tests.EngineOptions;

/// <summary>
/// 引擎配置选项测试：覆盖 LingFanEngineOptions 的两个行为方法（GetTargetFps / WriteSafeAreaToState），
/// 以及 LoggingOptions 默认值与 LoggingSinks 的 Flags 组合语义。
/// </summary>
public class EngineOptionsTests
{
    [Fact]
    public void GetTargetFps_ReturnsDesktopOnNonMobile()
    {
        // 测试宿主运行于 Windows（非 Android/iOS），故 GetTargetFps 走 DesktopTargetFps 分支。
        var opt = new LingFanEngineOptions { DesktopTargetFps = 120, MobileTargetFps = 60 };
        opt.GetTargetFps().Should().Be(120);
    }

    [Fact]
    public void GetTargetFps_MobileOverride_ReturnsMobileFps()
    {
        // 显式注入 Mobile 覆盖自动检测：目标帧率应取 MobileTargetFps（60）。
        var opt = new LingFanEngineOptions
        {
            RuntimePlatform = RuntimePlatform.Mobile,
            DesktopTargetFps = 120,
            MobileTargetFps = 60,
        };
        opt.GetTargetFps().Should().Be(60);
    }

    [Fact]
    public void GetTargetFps_DesktopOverride_ReturnsDesktopFps()
    {
        // 显式注入 Desktop 覆盖自动检测：目标帧率应取 DesktopTargetFps（120）。
        var opt = new LingFanEngineOptions
        {
            RuntimePlatform = RuntimePlatform.Desktop,
            DesktopTargetFps = 120,
            MobileTargetFps = 60,
        };
        opt.GetTargetFps().Should().Be(120);
    }

    [Fact]
    public void WriteSafeAreaToState_WritesFourKeys()
    {
        var opt = new LingFanEngineOptions
        {
            SafeAreaLeft = 1,
            SafeAreaTop = 2,
            SafeAreaRight = 3,
            SafeAreaBottom = 4,
        };

        IStateContainer state = new StateContainer();
        opt.WriteSafeAreaToState(state);

        state.Get<double>("safe_left").Should().Be(1);
        state.Get<double>("safe_top").Should().Be(2);
        state.Get<double>("safe_right").Should().Be(3);
        state.Get<double>("safe_bottom").Should().Be(4);
    }

    [Fact]
    public void LoggingOptions_Defaults()
    {
        var lo = new LoggingOptions();
        lo.Sinks.Should().Be(LoggingSinks.DebugTrace);
        lo.FileRetentionDays.Should().Be(7);
        lo.MirrorToDebugConsole.Should().BeTrue();
        lo.MirrorMinimumLevel.Should().Be(EngineLogLevel.Warning);
    }

    [Fact]
    public void LoggingSinks_FlagsCombine()
    {
        ((int)LoggingSinks.None).Should().Be(0);
        ((int)LoggingSinks.DebugTrace).Should().Be(1);
        ((int)LoggingSinks.Console).Should().Be(2);
        ((int)LoggingSinks.File).Should().Be(4);
        ((int)LoggingSinks.All).Should().Be(7);

        (LoggingSinks.All.HasFlag(LoggingSinks.DebugTrace)
         && LoggingSinks.All.HasFlag(LoggingSinks.Console)
         && LoggingSinks.All.HasFlag(LoggingSinks.File)).Should().BeTrue();
    }
}
