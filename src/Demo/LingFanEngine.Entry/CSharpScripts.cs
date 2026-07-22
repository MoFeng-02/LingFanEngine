using Avalonia.Controls.ApplicationLifetimes;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Entry.Demos;
using LingFanEngine.Services.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Entry;

/// <summary>
/// 启动层入口 — 注册教程场景 + 初始变量 + 命令
/// <para>引擎层不做游戏逻辑，所有游戏级配置在此定义</para>
/// <para>DSL button cmd="xxx" 在此注册对应的 C# 处理逻辑</para>
/// </summary>
public static class CSharpScripts
{
    public static void RegisterAll(IStateContainer state, ISceneRegistry sceneRegistry,
        IGameController ctrl, ICommandPipeline pipeline, ICommandService? cmdService,
        IAudioManager? audio = null,
        GameLoop? gameLoop = null,
        UI.OverlayManager? overlay = null,
        ISaveService? saveService = null)
    {
        // ===== 初始变量 =====
        // 已迁移到 DSL title_main 场景级 define（"你不认识他之前，他不存在于你的世界"）

        // ===== C# StoryScript 场景注册 =====
        // 演示 DSL → C# → DSL 双向导航 + C# 场景纳入回溯时间线
        var csTownIntro = new CsTownIntro();
        csTownIntro.Initialize(ctrl, state, pipeline, sceneRegistry);
        gameLoop?.RegisterScriptEntry(new SceneScriptEntry
        {
            SceneName = csTownIntro.SceneName,
            SceneType = csTownIntro.SceneType,
            Runner = () => csTownIntro.RunAsync(),
            Defines = csTownIntro.InDefines(),
            TimeEvents = csTownIntro.InTimeEvents()
        });

        // ===== 命令注册（DSL .story 中的 cmd="do_xxx" 依赖这些 C# 命令）=====
        if (cmdService == null) return;

        // ========== Overlay UI 桥接命令 ==========

        // 存档/读档面板
        cmdService.RegisterCommand("open_save", (_, _) =>
        {
            overlay?.ShowSavePanel();
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("open_load", (_, _) =>
        {
            overlay?.ShowLoadPanel();
            return Task.CompletedTask;
        });

        // 设置面板
        cmdService.RegisterCommand("open_settings", (_, _) =>
        {
            overlay?.ShowSettingsPanel();
            return Task.CompletedTask;
        });

        // CG 鉴赏面板
        cmdService.RegisterCommand("open_gallery", (_, _) =>
        {
            overlay?.ShowGalleryPanel();
            return Task.CompletedTask;
        });

        // 历史面板
        cmdService.RegisterCommand("open_history", (_, _) =>
        {
            overlay?.ShowHistoryPanel();
            return Task.CompletedTask;
        });

        // 继续游戏（从 quick_save 读取）
        cmdService.RegisterCommand("continue_game", async (_, _) =>
        {
            if (saveService != null)
            {
                var exists = await saveService.ExistsAsync("quick_save");
                if (exists)
                {
                    ctrl.Load("quick_save");
                    overlay?.HideAll();
                }
                else
                {
                    state.Set(StateKeys.Notify.Text, "没有找到快速存档");
                    state.Set(StateKeys.Notify.Type, "warning");
                }
            }
            else
            {
                state.Set(StateKeys.Notify.Text, "存档服务不可用");
                state.Set(StateKeys.Notify.Type, "error");
            }
        });

        // 返回标题
        cmdService.RegisterCommand("return_title", (_, _) =>
        {
            ctrl.Navigate("back_title");
            return Task.CompletedTask;
        });

        // 退出游戏
        cmdService.RegisterCommand("do_exit", (_, _) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else if (Avalonia.Application.Current?.ApplicationLifetime is IActivityApplicationLifetime singleView)
            {
                singleView.MainViewFactory?.Invoke();
            }
            return Task.CompletedTask;
        });

        // ========== 沙盒命令 ==========

        cmdService.RegisterCommand("do_gold_add", (_, _) =>
        {
            var g = state.Get<int>("player.gold") + 50;
            state.Set("player.gold", g);
            state.Set(StateKeys.Notify.Text, $"金币 +50（{g}）");
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("do_hp_add", (_, _) =>
        {
            var maxHp = state.Get<int>("player.maxHp");
            var _hp = state.Get<int>("player.hp");
            var hp = Math.Min(maxHp, _hp + 20);
            state.Set("player.hp", hp);
            state.Set(StateKeys.Notify.Text, $"HP +20（{hp}/{state.Get<int>("player.maxHp")}）");
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("do_dice", (_, _) =>
        {
            var v = Random.Shared.Next(1, 7);
            state.Set("sandbox.dice", v);
            state.Set(StateKeys.Notify.Text, $"掷出了 {v}！");
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("do_battle", (_, _) =>
        {
            var dmg = Random.Shared.Next(5, 21);
            var player_hp = state.Get<int>("player.hp");
            var hp = Math.Max(0, player_hp - dmg);
            state.Set("player.hp", hp);
            state.Set(StateKeys.Notify.Text, $"受到 {dmg} 伤害（HP:{hp}）");
            return Task.CompletedTask;
        });

        // ========== 音频命令 ==========

        cmdService.RegisterCommand("do_bgm", (_, _) =>
        { pipeline.SendAsync(new PlayBgmCommand { Path = "Audio/crickets_night01.mp3", Volume = 0.8f }); return Task.CompletedTask; });
        cmdService.RegisterCommand("do_stop_bgm", (_, _) =>
        { pipeline.SendAsync(new StopBgmCommand()); return Task.CompletedTask; });

        // SE 音效
        cmdService.RegisterCommand("do_play_se", (_, _) =>
        { pipeline.SendAsync(new PlaySeCommand { Path = "Audio/crickets_night01.mp3", Volume = 1.2f }); return Task.CompletedTask; });

        // 静音切换
        cmdService.RegisterCommand("do_mute", (_, _) =>
        {
            if (audio != null) audio.MasterMuted = !audio.MasterMuted;
            state.Set(StateKeys.Notify.Text, audio != null && audio.MasterMuted ? "🔇 已静音" : "🔊 已取消静音");
            return Task.CompletedTask;
        });

        // ========== 视频命令 ==========

        cmdService.RegisterCommand("do_play_video", (_, _) =>
        { pipeline.SendAsync(new PlayVideoCommand { Path = "Video/m1.mp4", Volume = 0.8f }); return Task.CompletedTask; });
        cmdService.RegisterCommand("do_stop_video", (_, _) =>
        { pipeline.SendAsync(new StopVideoCommand()); return Task.CompletedTask; });
        cmdService.RegisterCommand("do_pause_video", (_, _) =>
        { pipeline.SendAsync(new PauseVideoCommand()); return Task.CompletedTask; });
        cmdService.RegisterCommand("do_resume_video", (_, _) =>
        { pipeline.SendAsync(new ResumeVideoCommand()); return Task.CompletedTask; });
        cmdService.RegisterCommand("do_seek_video", (_, _) =>
        { pipeline.SendAsync(new SeekVideoCommand { Position = 10 }); return Task.CompletedTask; });

        // 快速存档/读档
        cmdService.RegisterCommand("do_save", (_, _) =>
        { pipeline.SendAsync(new SaveLoadCommand { SlotId = "quick_save", IsSave = true }); return Task.CompletedTask; });
        cmdService.RegisterCommand("do_load", (_, _) =>
        { pipeline.SendAsync(new SaveLoadCommand { SlotId = "quick_save", IsSave = false }); return Task.CompletedTask; });
    }
}
