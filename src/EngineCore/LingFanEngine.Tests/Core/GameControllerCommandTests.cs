using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;
using LingFanEngine.Tests.Fakes;
using Xunit;

namespace LingFanEngine.Tests.Core;

/// <summary>
/// GameController「薄包装」命令投递 + 纯状态方法测试（Phase 3 补测）。
/// <para>
/// 覆盖 fire-and-forget 版 API：断言经 <see cref="FakeCommandPipeline"/> 发出的命令类型与关键字段正确；
/// 以及不发命令、只写 StateContainer 的偏好/开关/查询类方法。
/// 这些方法此前几乎无覆盖，是把 GameController 覆盖率从个位数拉起来的最快批次（无需 headless/宿主）。
/// </para>
/// </summary>
public class GameControllerCommandTests
{
    private static (GameController gc, FakeCommandPipeline fake, StateContainer state) NewFake()
    {
        var fake = new FakeCommandPipeline();
        var host = new EngineTestHost(pipeline: fake);
        return (host.GameController, fake, host.State);
    }

    private static (GameController gc, StateContainer state) NewState()
    {
        var host = new EngineTestHost();
        return (host.GameController, host.State);
    }

    // ==================== 导航 / 变量 ====================

    [Fact]
    public void Navigate_SendsNavigateCommand()
    {
        var (gc, fake, _) = NewFake();
        gc.Navigate("chapter1");
        fake.Sent.OfType<NavigateCommand>().Should().ContainSingle(c => c.Path == "chapter1");
    }

    [Fact]
    public void Set_SendsSetVariableCommand_NotDefine()
    {
        var (gc, fake, _) = NewFake();
        gc.Set("gold", 100);
        var cmd = fake.Sent.OfType<SetVariableCommand>().Should().ContainSingle().Subject;
        cmd.Key.Should().Be("gold");
        cmd.Value.Should().Be(100);
        cmd.IsDefine.Should().BeFalse();
    }

    [Fact]
    public void Define_SendsSetVariableCommand_WithIsDefineTrue()
    {
        var (gc, fake, _) = NewFake();
        gc.Define("maxHp", 999);
        fake.Sent.OfType<SetVariableCommand>().Should().ContainSingle(c => c.Key == "maxHp" && c.IsDefine);
    }

    [Fact]
    public void MergeDefSets_SendsMergeDefinesCommand_WithDict()
    {
        var (gc, fake, _) = NewFake();
        var dict = new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x" };
        gc.MergeDefSets(dict);
        fake.Sent.OfType<MergeDefinesCommand>().Should().ContainSingle(c => c.Defines == dict);
    }

    // ==================== 对话 / 过渡 / 等待 ====================

    [Fact]
    public void Say_FireAndForget_SendsShowDialogCommand()
    {
        var (gc, fake, _) = NewFake();
        gc.Say("你好", "旁白", typewriter: false, clickable: true);
        var cmd = fake.Sent.OfType<ShowDialogCommand>().Should().ContainSingle().Subject;
        cmd.Text.Should().Be("你好");
        cmd.Speaker.Should().Be("旁白");
        cmd.TypewriterEnabled.Should().BeFalse();
        cmd.Clickable.Should().BeTrue();
    }

    [Fact]
    public void Transition_SendsTransitionCommand_WithTypeAndDuration()
    {
        var (gc, fake, _) = NewFake();
        gc.Transition("FadeIn", 0.8);
        fake.Sent.OfType<TransitionCommand>().Should().ContainSingle(c => c.Type == "FadeIn" && c.Duration == 0.8);
    }

    [Fact]
    public void Wait_SendsWaitCommand_WithSeconds()
    {
        var (gc, fake, _) = NewFake();
        gc.Wait(1.5);
        fake.Sent.OfType<WaitCommand>().Should().ContainSingle(c => c.Seconds == 1.5);
    }

    // ==================== 音频 ====================

    [Fact]
    public void PlayBgm_SendsPlayBgmCommand_WithPathVolumeFade()
    {
        var (gc, fake, _) = NewFake();
        gc.PlayBgm("bgm/town.ogg", 0.5f, fadeIn: 2.0);
        var cmd = fake.Sent.OfType<PlayBgmCommand>().Should().ContainSingle().Subject;
        cmd.Path.Should().Be("bgm/town.ogg");
        cmd.Volume.Should().Be(0.5f);
        cmd.FadeIn.Should().Be(2.0);
    }

    [Fact]
    public void StopBgm_SendsPlayBgmCommand_WithEmptyPathAndFadeOut()
    {
        var (gc, fake, _) = NewFake();
        gc.StopBgm(fadeOut: 1.0);
        var cmd = fake.Sent.OfType<PlayBgmCommand>().Should().ContainSingle().Subject;
        cmd.Path.Should().BeEmpty();
        cmd.FadeOut.Should().Be(1.0);
    }

    [Fact]
    public void PlaySe_And_StopSe()
    {
        var (gc, fake, _) = NewFake();
        gc.PlaySe("se/click.wav", 0.7f);
        gc.StopSe();
        var se = fake.Sent.OfType<PlaySeCommand>().ToList();
        se.Should().HaveCount(2);
        se[0].Path.Should().Be("se/click.wav");
        se[0].Volume.Should().Be(0.7f);
        se[1].Path.Should().BeEmpty();
    }

    [Fact]
    public void PlayAmbient_And_StopAmbient()
    {
        var (gc, fake, _) = NewFake();
        gc.PlayAmbient("amb/rain.ogg", 0.4f, loop: false);
        gc.StopAmbient();
        fake.Sent.OfType<PlayAmbientCommand>().Should().ContainSingle(c => c.Path == "amb/rain.ogg" && c.Volume == 0.4f && !c.Loop);
        fake.Sent.OfType<StopAmbientCommand>().Should().ContainSingle();
    }

    [Fact]
    public void PlayVoice_And_StopVoice()
    {
        var (gc, fake, _) = NewFake();
        gc.PlayVoice("voice/a.wav", 0.9f);
        gc.StopVoice();
        fake.Sent.OfType<PlayVoiceCommand>().Should().ContainSingle(c => c.Path == "voice/a.wav" && c.Volume == 0.9f);
        fake.Sent.OfType<StopVoiceCommand>().Should().ContainSingle();
    }

    // ==================== 场景元素 ====================

    [Fact]
    public void Show_SendsShowHideCommand_IsShowTrue()
    {
        var (gc, fake, _) = NewFake();
        gc.Show("alice", 100, 200);
        var cmd = fake.Sent.OfType<ShowHideCommand>().Should().ContainSingle().Subject;
        cmd.Target.Should().Be("alice");
        cmd.X.Should().Be(100);
        cmd.Y.Should().Be(200);
        cmd.IsShow.Should().BeTrue();
        cmd.IsBackground.Should().BeFalse();
    }

    [Fact]
    public void Hide_SendsShowHideCommand_IsShowFalse()
    {
        var (gc, fake, _) = NewFake();
        gc.Hide("alice");
        fake.Sent.OfType<ShowHideCommand>().Should().ContainSingle(c => c.Target == "alice" && !c.IsShow);
    }

    [Fact]
    public void Background_SendsShowHideCommand_IsBackgroundTrue()
    {
        var (gc, fake, _) = NewFake();
        gc.Background("bg/room.png");
        fake.Sent.OfType<ShowHideCommand>().Should().ContainSingle(c => c.Target == "bg/room.png" && c.IsShow && c.IsBackground);
    }

    // ==================== 堆栈 / 回溯 / 存档 ====================

    [Fact]
    public void Back_Forward_Rollback_Rollforward_SendCorrectCommands()
    {
        var (gc, fake, _) = NewFake();
        gc.Back();
        gc.Forward();
        gc.Rollback();
        gc.Rollforward();
        fake.Sent.OfType<BackCommand>().Should().ContainSingle();
        fake.Sent.OfType<ForwardCommand>().Should().ContainSingle();
        fake.Sent.OfType<RollbackCommand>().Should().ContainSingle();
        fake.Sent.OfType<RollforwardCommand>().Should().ContainSingle();
    }

    [Fact]
    public void RollbackTo_SendsRollbackToCommand_WithIndex()
    {
        var (gc, fake, _) = NewFake();
        gc.RollbackTo(3);
        fake.Sent.OfType<RollbackToCommand>().Should().ContainSingle(c => c.TargetCheckpointIndex == 3);
    }

    [Fact]
    public void Save_And_Load_SendSaveLoadCommand()
    {
        var (gc, fake, _) = NewFake();
        gc.Save("slot1");
        gc.Load("slot1");
        var cmds = fake.Sent.OfType<SaveLoadCommand>().ToList();
        cmds.Should().HaveCount(2);
        cmds[0].SlotId.Should().Be("slot1");
        cmds[0].IsSave.Should().BeTrue();
        cmds[1].IsSave.Should().BeFalse();
    }

    [Fact]
    public void ClearStack_And_ResetGameState()
    {
        var (gc, fake, _) = NewFake();
        gc.ClearStack();
        gc.ResetGameState();
        fake.Sent.OfType<ClearStackCommand>().Should().ContainSingle();
        fake.Sent.OfType<ResetGameStateCommand>().Should().ContainSingle();
    }

    // ==================== 屏幕震动 / 跳过 / 自动 ====================

    [Fact]
    public void Shake_SendsShakeCommand()
    {
        var (gc, fake, _) = NewFake();
        gc.Shake(20, 1.0);
        fake.Sent.OfType<ShakeCommand>().Should().ContainSingle(c => c.Intensity == 20 && c.Duration == 1.0);
    }

    [Fact]
    public void ToggleSkip_And_ToggleAuto()
    {
        var (gc, fake, _) = NewFake();
        gc.ToggleSkip();
        gc.ToggleAuto();
        fake.Sent.OfType<ToggleSkipCommand>().Should().ContainSingle();
        fake.Sent.OfType<ToggleAutoCommand>().Should().ContainSingle();
    }

    // ==================== 通知 / NVL ====================

    [Fact]
    public void Notify_SendsNotifyCommand_DefaultDurationWhenZero()
    {
        var (gc, fake, _) = NewFake();
        gc.Notify("已保存", "info");   // duration 省略 → 走默认 3.0
        var cmd = fake.Sent.OfType<NotifyCommand>().Should().ContainSingle().Subject;
        cmd.Text.Should().Be("已保存");
        cmd.Type.Should().Be("info");
        cmd.Duration.Should().Be(3.0);
    }

    [Fact]
    public void Notify_UsesExplicitDuration()
    {
        var (gc, fake, _) = NewFake();
        gc.Notify("警告", "warning", 5.0);
        fake.Sent.OfType<NotifyCommand>().Should().ContainSingle(c => c.Duration == 5.0 && c.Type == "warning");
    }

    [Fact]
    public void EnterNvl_ClearNvl_ExitNvl_SendNvlCommand_WithFlags()
    {
        var (gc, fake, _) = NewFake();
        gc.EnterNvl();
        gc.ClearNvl();
        gc.ExitNvl();
        var cmds = fake.Sent.OfType<NvlCommand>().ToList();
        cmds.Should().HaveCount(3);
        cmds[0].IsClear.Should().BeFalse();
        cmds[0].IsExit.Should().BeFalse();
        cmds[1].IsClear.Should().BeTrue();
        cmds[2].IsExit.Should().BeTrue();
    }

    // ==================== CG 鉴赏 ====================

    [Fact]
    public void UnlockGallery_SendsUnlockGalleryCommand()
    {
        var (gc, fake, _) = NewFake();
        gc.UnlockGallery("cg01", "cg/01.png", "初见");
        var cmd = fake.Sent.OfType<UnlockGalleryCommand>().Should().ContainSingle().Subject;
        cmd.Id.Should().Be("cg01");
        cmd.ImagePath.Should().Be("cg/01.png");
        cmd.Title.Should().Be("初见");
    }

    [Fact]
    public void IsGalleryUnlocked_FalseWhenEmpty_GetGalleryUnlocked_Empty()
    {
        var (gc, _) = NewState();
        gc.IsGalleryUnlocked("cg01").Should().BeFalse();
        gc.GetGalleryUnlocked().Should().BeEmpty();
    }

    // ==================== 时间事件 ====================

    [Fact]
    public void RegisterTimeEvent_SendsTimeEventCommand()
    {
        var (gc, fake, _) = NewFake();
        gc.RegisterTimeEvent("evt_boss", triggerDay: 5, triggerHour: 12);
        var cmd = fake.Sent.OfType<TimeEventCommand>().Should().ContainSingle().Subject;
        cmd.Target.Should().Be("evt_boss");
        cmd.TriggerDay.Should().Be(5);
        cmd.TriggerHour.Should().Be(12);
        cmd.IsOneShot.Should().BeTrue();
    }

    [Fact]
    public void RegisterDailyEvent_SendsRecurringTimeEvent_Day0()
    {
        var (gc, fake, _) = NewFake();
        gc.RegisterDailyEvent("morning", triggerHour: 8);
        var cmd = fake.Sent.OfType<TimeEventCommand>().Should().ContainSingle().Subject;
        cmd.TriggerDay.Should().Be(0);
        cmd.TriggerHour.Should().Be(8);
        cmd.IsOneShot.Should().BeFalse();
    }

    [Fact]
    public void RegisterWeeklyEvent_SendsTimeEvent_WithDaysOfWeek()
    {
        var (gc, fake, _) = NewFake();
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Friday };
        gc.RegisterWeeklyEvent("class", days, triggerHour: 9);
        var cmd = fake.Sent.OfType<TimeEventCommand>().Should().ContainSingle().Subject;
        cmd.DaysOfWeek.Should().BeEquivalentTo(days);
        cmd.TriggerHour.Should().Be(9);
    }

    [Fact]
    public void UnregisterEvent_RestoreEvent_TimePauseResumeSkip()
    {
        var (gc, fake, _) = NewFake();
        gc.UnregisterEvent("e1");
        gc.RestoreEvent("e1");
        gc.PauseGameTime();
        gc.ResumeGameTime();
        gc.SkipTime(30);
        fake.Sent.OfType<UnregisterTimeEventCommand>().Should().ContainSingle(c => c.Id == "e1");
        fake.Sent.OfType<RestoreTimeEventCommand>().Should().ContainSingle(c => c.Id == "e1");
        fake.Sent.OfType<TimePauseCommand>().Should().ContainSingle();
        fake.Sent.OfType<TimeResumeCommand>().Should().ContainSingle();
        fake.Sent.OfType<SkipTimeCommand>().Should().ContainSingle(c => c.Minutes == 30);
    }

    // ==================== 视频 ====================

    [Fact]
    public void PlayVideo_SendsPlayVideoCommand()
    {
        var (gc, fake, _) = NewFake();
        gc.PlayVideo("mv/op.mp4", 0.6f, loop: true, autoPlay: false);
        var cmd = fake.Sent.OfType<PlayVideoCommand>().Should().ContainSingle().Subject;
        cmd.Path.Should().Be("mv/op.mp4");
        cmd.Volume.Should().Be(0.6f);
        cmd.Loop.Should().BeTrue();
        cmd.AutoPlay.Should().BeFalse();
    }

    [Fact]
    public void StopPauseResumeVideo_SendCommands()
    {
        var (gc, fake, _) = NewFake();
        gc.StopVideo();
        gc.PauseVideo();
        gc.ResumeVideo();
        fake.Sent.OfType<StopVideoCommand>().Should().ContainSingle();
        fake.Sent.OfType<PauseVideoCommand>().Should().ContainSingle();
        fake.Sent.OfType<ResumeVideoCommand>().Should().ContainSingle();
    }

    [Fact]
    public void SeekVideo_SendsSeekVideoCommand_WithTotalSeconds()
    {
        var (gc, fake, _) = NewFake();
        gc.SeekVideo(TimeSpan.FromSeconds(42));
        fake.Sent.OfType<SeekVideoCommand>().Should().ContainSingle(c => c.Position == 42);
    }

    // ==================== 调试 ====================

    [Fact]
    public void DebugLog_SendsDebugLogCommand()
    {
        var (gc, fake, _) = NewFake();
        gc.DebugLog("hello", "Warning");
        fake.Sent.OfType<DebugLogCommand>().Should().ContainSingle(c => c.Message == "hello" && c.Level == "Warning");
    }

    // ==================== 偏好设置（纯状态） ====================

    [Theory]
    [InlineData("master", StateKeys.Preferences.MasterVolume)]
    [InlineData("bgm", StateKeys.Preferences.BgmVolume)]
    [InlineData("se", StateKeys.Preferences.SeVolume)]
    [InlineData("voice", StateKeys.Preferences.VoiceVolume)]
    public void SetVolume_And_GetVolume_RoundTrip(string channel, string key)
    {
        var (gc, state) = NewState();
        gc.SetVolume(channel, 0.42f);
        state.Get<float>(key).Should().BeApproximately(0.42f, 1e-6f);
        gc.GetVolume(channel).Should().BeApproximately(0.42f, 1e-6f);
    }

    [Fact]
    public void SetVolume_ClampsToZeroOne()
    {
        var (gc, _) = NewState();
        gc.SetVolume("master", 3.5f);
        gc.GetVolume("master").Should().Be(1f);
        gc.SetVolume("master", -1f);
        gc.GetVolume("master").Should().Be(0f);
    }

    [Fact]
    public void SetMuted_WritesPreferenceKey()
    {
        var (gc, state) = NewState();
        gc.SetMuted(true);
        state.Get<bool>(StateKeys.Preferences.MasterMuted).Should().BeTrue();
    }

    [Fact]
    public void SetTextSpeed_And_SetAutoDelay_WriteState()
    {
        var (gc, state) = NewState();
        gc.SetTextSpeed(60);
        gc.SetAutoDelay(2.5);
        state.Get<double>(StateKeys.Preferences.TextSpeed).Should().Be(60);
        state.Get<double>(StateKeys.Playback.AutoDelay).Should().Be(2.5);
    }

    // ==================== 历史 / 鉴赏面板开关（纯状态 toggle） ====================

    [Fact]
    public void ToggleHistory_FlipsVisibility()
    {
        var (gc, state) = NewState();
        state.Get<bool>(StateKeys.History.Visible).Should().BeFalse();
        gc.ToggleHistory();
        state.Get<bool>(StateKeys.History.Visible).Should().BeTrue();
        gc.ToggleHistory();
        state.Get<bool>(StateKeys.History.Visible).Should().BeFalse();
    }

    [Fact]
    public void ClearHistory_And_GetHistory()
    {
        var (gc, _) = NewState();
        gc.GetHistory().Should().BeEmpty();
        gc.ClearHistory();
        gc.GetHistory().Should().BeEmpty();
    }

    [Fact]
    public void ToggleGallery_FlipsVisibility()
    {
        var (gc, state) = NewState();
        gc.ToggleGallery();
        state.Get<bool>(StateKeys.Gallery.Visible).Should().BeTrue();
        gc.ToggleGallery();
        state.Get<bool>(StateKeys.Gallery.Visible).Should().BeFalse();
    }

    // ==================== NVL 查询 ====================

    [Fact]
    public void IsNvlActive_And_GetNvlText_ReadState()
    {
        var (gc, state) = NewState();
        gc.IsNvlActive.Should().BeFalse();
        gc.GetNvlText().Should().BeEmpty();

        state.Set(StateKeys.Nvl.Active, true);
        state.Set(StateKeys.Nvl.Text, "累积文本");
        gc.IsNvlActive.Should().BeTrue();
        gc.GetNvlText().Should().Be("累积文本");
    }

    // ==================== Phase 24: 回溯闸门 / 窗口模式 / Screen ====================

    [Fact]
    public void BlockRollback_SetsBlockedUntilToCurrentIndex_FixRollbackResetsToMinusOne()
    {
        var (gc, state) = NewState();
        state.Set(StateKeys.Dsl.CurrentIndex, 7);
        gc.BlockRollback();
        state.Get<int>(StateKeys.Rollback.BlockedUntil).Should().Be(7);

        gc.FixRollback();
        state.Get<int>(StateKeys.Rollback.BlockedUntil).Should().Be(-1);
    }

    [Fact]
    public void ShowWindow_HideWindow_SetWindowAuto_WriteWindowMode()
    {
        var (gc, state) = NewState();
        gc.ShowWindow();
        state.Get<string>(StateKeys.Dialog.WindowMode).Should().Be("show");
        gc.HideWindow();
        state.Get<string>(StateKeys.Dialog.WindowMode).Should().Be("hide");
        gc.SetWindowAuto();
        state.Get<string>(StateKeys.Dialog.WindowMode).Should().Be("auto");
    }

    [Fact]
    public void HasScreen_And_GetCurrentScreen()
    {
        var (gc, state) = NewState();
        gc.GetCurrentScreen().Should().BeNull();
        gc.HasScreen("settings").Should().BeFalse();

        state.Set(StateKeys.Screen.ActiveScreen, "settings");
        gc.GetCurrentScreen().Should().Be("settings");
        gc.HasScreen("SETTINGS").Should().BeTrue();   // 大小写不敏感
        gc.HasScreen("other").Should().BeFalse();
    }

    [Fact]
    public void GetScreenParam_ReturnsTypedValue_OrDefaultWhenMissing()
    {
        var (gc, state) = NewState();
        state.Set(StateKeys.Screen.Params, new Dictionary<string, object?> { ["level"] = 5, ["name"] = "boss" });
        gc.GetScreenParam<int>("level").Should().Be(5);
        gc.GetScreenParam<string>("name").Should().Be("boss");
        gc.GetScreenParam<int>("missing").Should().Be(0);   // 缺键 → default
    }

    [Fact]
    public void SetScreenResult_WritesScreenResultKey()
    {
        var (gc, state) = NewState();
        gc.SetScreenResult("ok");
        state.Get<string>(StateKeys.Screen.Result).Should().Be("ok");
    }

    // ==================== 角色定义（纯状态） ====================

    [Fact]
    public void DefineCharacter_WritesCharacterPropsUnderPrefix()
    {
        var (gc, state) = NewState();
        gc.DefineCharacter("boss", name: "魔王", color: "#FF4444");
        var props = state.Get<Dictionary<string, object?>>(StateKeys.Characters.Prefix + "boss");
        props.Should().NotBeNull();
        props!["name"].Should().Be("魔王");
        props["color"].Should().Be("#FF4444");
    }
}
