﻿using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Games;

namespace LingFanEngine.Entry.Demos;

/// <summary>标题画面</summary>
public class TutorialTitle : StoryScript
{
    public override string SceneName => "tutorial_title";
    public override SceneType SceneType => SceneType.Menu;

    //ctrl.Define("player.name", "你");
    //ctrl.Define("player.gold", 100);
    //ctrl.Define("player.hp", 50);
    //ctrl.Define("player.maxHp", 100);
    //ctrl.Define("sandbox.dice", 0);
    public override Dictionary<string, object?> InDefines() => new Dictionary<string, object?>() {
        {
            "player",new Dictionary<string,object>()
            {
                { "name", "你" },
                { "gold", 100 },
                { "hp", 50 },
                { "maxHp", 100 },
            }
        },
        { "sandbox.dice", 0 }
    };
    public override async Task Run()
    {
        SetScene("Images/door_zoom.jpg", "灵泛引擎 · 入门教程", 1);
        AddText("基于 C# + Avalonia 的视觉小说引擎", 0, 170, 16, "#AAAAAA", halign: "center");
        AddButton("开始教程", 0, 340, 240, 44, nav: "tutorial_chapter", halign: "center", color: "#88CCFF");
        AddButton("特色演示", 0, 410, 240, 44, nav: "tutorial_features", halign: "center", color: "#FFCC88");
        AddButton("沙盒测试", 0, 480, 240, 44, nav: "tutorial_sandbox", halign: "center", color: "#88FF88");
        AddButton("退出游戏", 0, 550, 240, 44, cmd: "do_exit", halign: "center", color: "#FF8888");
        AddText("Space/Enter=推进对话  Esc=返回标题  F5=存档  F9=读档", 0, 640, 12, "#666666", halign: "center");
        await Task.CompletedTask;
    }
}

/// <summary>第一章 — 对话流 + 内联标记 + 菜单选择</summary>
public class TutorialChapter : StoryScript
{
    public override string SceneName => "tutorial_chapter";
    public override SceneType SceneType => SceneType.Game;

    public override async Task Run()
    {
        SetScene("Images/door_zoom.jpg", "第一章 · 认识灵泛引擎");
        await Ctrl.TransitionAsync("FadeIn", 0.5);

        await Ctrl.SayAsync("你好！欢迎使用灵泛引擎。", "系统");
        await Ctrl.SayAsync("{b}灵泛引擎{/b} 是一个基于 {color=#88CCFF}C# + Avalonia{/color} 的视觉小说引擎。",
            "系统", textColor: "#CCFFCC");
        await Ctrl.SayAsync("支持 {color=#FFD700}对话{/color}、{color=#FF8888}选择{/color}、过渡动画、存档等。", "系统");

        // 菜单选择（AddMenu 对标 Ren'Py menu）
        AddMenu("接下来要体验什么？",
            ("进入教程第二章", "tutorial_features"),
            ("进入沙盒测试", "tutorial_sandbox"),
            ("返回标题", "tutorial_title"));
    }
}

/// <summary>第二章 — 过渡动画 + 图片 show/hide + 音效</summary>
public class TutorialFeatures : StoryScript
{
    public override string SceneName => "tutorial_features";
    public override SceneType SceneType => SceneType.Game;
    public override Dictionary<string, object?> InDefines() => new();

    public override async Task Run()
    {
        // 1. 过渡效果演示
        SetScene("Images/door_zoom.jpg", "第二章 · 过渡与动画");
        await Ctrl.TransitionAsync("FadeIn", 0.6);
        await Ctrl.SayAsync("灵泛引擎支持多种 {color=#FFD700}过渡效果{/color}。", "系统");

        // SlideLeftIn
        SetScene("Images/door_zoom.jpg", "SlideLeftIn — 左滑入");
        await Ctrl.TransitionAsync("SlideLeftIn", 0.8);
        await Ctrl.SayAsync("这是 SlideLeftIn 过渡。");

        // ZoomIn
        SetScene("Images/door_zoom.jpg", "ZoomIn — 缩放入");
        await Ctrl.TransitionAsync("ZoomIn", 0.8);
        await Ctrl.SayAsync("这是 ZoomIn 过渡。");

        // 2. 图片 show/hide 演示
        SetScene("Images/door_zoom.jpg", "Show/Hide 立绘演示", bgOpacity: 0.2);
        await Ctrl.SayAsync("接下来演示 {b}show/hide{/b} 立绘。");

        await Ctrl.ShowAsync("Images/door_zoom.jpg", 200, 100);
        await Ctrl.SayAsync("立绘显示在左侧。");
        await Ctrl.HideAsync("Images/door_zoom.jpg");
        await Ctrl.SayAsync("立绘已隐藏。");

        await Ctrl.ShowAsync("Images/door_zoom.jpg", 800, 150);
        await Ctrl.SayAsync("立绘移动到右侧。");

        // 3. 音频演示
        await Ctrl.SayAsync("引擎使用 {b}Master → BGM/SE/Voice{/b} 三层音频架构。", "系统");
        await Ctrl.PlayBgmAsync("Audio/crickets_night01.mp3", 0.4f);
        await Ctrl.SayAsync("正在播放 BGM（可循环叠加多个）...", textColor: "#CCFFCC");

        await Ctrl.SayAsync("SE 音效独立于 BGM：");
        _ = Ctrl.PlaySeAsync("Audio/chest_drawer_open.mp3", 0.5f);
        await Ctrl.SayAsync("BGM 和 SE 同时播放。音频生命周期由开发者手动控制。", "系统");

        await Ctrl.StopBgmAsync();
        await Ctrl.SayAsync("开发者调用 StopBgmAsync() 停止 BGM。");

        // 回到正常遮挡
        SetScene("Images/door_zoom.jpg", "第二章 · 结束");
        AddMenu("接下来？",
            ("进入沙盒测试", "tutorial_sandbox"),
            ("返回标题", "tutorial_title"));
    }
}

/// <summary>沙盒测试 — 命令按钮 + 变量实时显示</summary>
public class TutorialSandbox : StoryScript
{
    public override string SceneName => "tutorial_sandbox";
    public override SceneType SceneType => SceneType.Game;
    public override async Task Run()
    {
        SetScene("Images/door_zoom.jpg", "沙盒测试", 0.3);

        await Ctrl.SayAsync("{b}沙盒模式{/b}：点击下方按钮观察变量实时变化。", "系统",
            typewriter: false);

        // 变量显示区
        AddText("{player.gold} 金  HP:{player.hp}/{player.maxHp}", 20, 140, 16, "#FFFFFF", font: "Consolas");
        AddText("骰子: {sandbox.dice}", 20, 168, 16, "#888888", font: "Consolas");

        // 操作按钮区
        AddButton("金币 +50", 20, 240, 130, 40, cmd: "do_gold_add", color: "#88FF88");
        AddButton("HP +20", 170, 240, 130, 40, cmd: "do_hp_add", color: "#FF88AA");
        AddButton("掷骰子", 320, 240, 130, 40, cmd: "do_dice", color: "#FFAA88");
        AddButton("受击", 470, 240, 130, 40, cmd: "do_battle", color: "#FF4444");

        AddButton("存档", 20, 300, 130, 40, cmd: "do_save", color: "#88FF88");
        AddButton("读档", 170, 300, 130, 40, cmd: "do_load", color: "#88CCFF");
        AddButton("BGM 开", 320, 300, 130, 40, cmd: "do_bgm", color: "#AA88FF");
        AddButton("BGM 关", 470, 300, 130, 40, cmd: "do_stop_bgm", color: "#AA88FF");
        AddButton("播放 SE", 170, 360, 130, 40, cmd: "do_play_se", color: "#FFCC88");
        AddButton("静音切换", 320, 360, 280, 40, cmd: "do_mute", color: "#CC88CC", halign: "center");

        AddButton("返回标题", 0, 420, 160, 40, nav: "tutorial_title",
            halign: "center", color: "#FF8888");
        AddText("Space=推进  Esc=返回标题  F5=存档  F9=读档", 0, 460, 12, "#666666", halign: "center");

        Ctrl.Set(StateKeys.Dialog.Text, "");
    }
}