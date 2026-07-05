﻿﻿using System;
using System.Threading.Tasks;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Entry.Demos;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Core;
using LingFanEngine.Games;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Entry;

/// <summary>
/// 启动层入口 — 注册教程场景 + 初始变量 + 命令
/// <para>引擎层不做游戏逻辑，所有游戏级配置在此定义</para>
/// </summary>
public static class CSharpScripts
{
    public static void RegisterAll(IStateContainer state, ISceneRegistry sceneRegistry,
        GameController ctrl, ICommandPipeline pipeline, ICommandService? cmdService,
        LingFanEngine.Services.Media.AudioManager? audio = null,
        GameLoop? gameLoop = null)
    {
        // ===== 初始变量 =====
        //ctrl.Define("player.name", "你");
        //ctrl.Define("player.gold", 100);
        //ctrl.Define("player.hp", 50);
        //ctrl.Define("player.maxHp", 100);
        //ctrl.Define("sandbox.dice", 0);
        // 已迁移到场景级变量定义

        // ===== 教程场景 =====
        var scripts = new StoryScript[]
        {
            new TutorialTitle(),
            new TutorialChapter(),
            new TutorialFeatures(),
            new TutorialSandbox(),
        };

        foreach (var script in scripts)
        {
            script.Initialize(ctrl, state, pipeline, sceneRegistry);
            gameLoop?.RegisterScriptEntry(new SceneScriptEntry
            {
                SceneName = script.SceneName,
                SceneType = script.SceneType,
                Runner = () => script.Run(),
                Defines = script.InDefines()
            });
        }

        // ===== 命令注册（引擎层无预制命令，全部在此） =====
        if (cmdService == null) return;

        cmdService.RegisterCommand("do_exit", (_, _) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            return Task.CompletedTask;
        });

        // 沙盒命令
        cmdService.RegisterCommand("do_gold_add", (_, _) =>
        {
            var g = state.Get<int>("player.gold") + 50;
            state.Set("player.gold", g);
            state.Set<object>(StateKeys.Notify.Text, $"金币 +50（{g}）");
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("do_hp_add", (_, _) =>
        {
            var hp = Math.Min(state.Get<int>("player.maxHp"), state.Get<int>("player.hp") + 20);
            state.Set("player.hp", hp);
            state.Set<object>(StateKeys.Notify.Text, $"HP +20（{hp}/{state.Get<int>("player.maxHp")}）");
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("do_dice", (_, _) =>
        {
            var v = Random.Shared.Next(1, 7);
            state.Set("sandbox.dice", v);
            state.Set<object>(StateKeys.Notify.Text, $"掷出了 {v}！");
            return Task.CompletedTask;
        });
        cmdService.RegisterCommand("do_battle", (_, _) =>
        {
            var dmg = Random.Shared.Next(5, 21);
            var player_hp = state.Get<int>("player.hp");
            var hp = Math.Max(0, player_hp - dmg);
            state.Set("player.hp", hp);
            state.Set<object>(StateKeys.Notify.Text, $"受到 {dmg} 伤害（HP:{hp}）");
            return Task.CompletedTask;
        });

        // BGM
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
            state.Set<object>(StateKeys.Notify.Text, audio != null && audio.MasterMuted ? "🔇 已静音" : "🔊 已取消静音");
            return Task.CompletedTask;
        });

        // 存档
        cmdService.RegisterCommand("do_save", (_, _) =>
        { pipeline.SendAsync(new SaveLoadCommand { SlotId = "quick_save", IsSave = true }); return Task.CompletedTask; });
        cmdService.RegisterCommand("do_load", (_, _) =>
        { pipeline.SendAsync(new SaveLoadCommand { SlotId = "quick_save", IsSave = false }); return Task.CompletedTask; });
    }
}