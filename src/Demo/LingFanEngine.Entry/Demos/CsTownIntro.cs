using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Games;

namespace LingFanEngine.Entry.Demos;

/// <summary>
/// C# StoryScript 演示场景 — 小镇入口前奏
/// <para>演示 DSL → C# → DSL 双向导航 + C# 场景纳入回溯时间线</para>
/// <para>流程：title_main(prologue) → cs_town_intro(C#) → town_entrance(DSL)</para>
/// <para>C# 场景为 Game 类型，创建场景级检查点（非逐句），滚轮回溯可回到此场景</para>
/// </summary>
public class CsTownIntro : StoryScript
{
    public override string SceneName => "cs_town_intro";
    public override SceneType SceneType => SceneType.Game;

    public override async Task RunAsync()
    {
        // 设置场景背景 + 标题
        SetScene("Images/door_zoom.jpg", "小镇入口（C# 场景）", bgOpacity: 0.4);

        // 提示信息
        AddText("这段剧情由 C# StoryScript 驱动", 50, 400, 16, "#CCCCCC", halign: "center");

        // 过渡动画
        await Ctrl.TransitionAsync("FadeIn", 0.5);

        // C# 场景对话（通过管道发送 ShowDialogCommand，不创建逐句检查点）
        await Ctrl.SayAsync("你从小路走来，眼前是一座雾气缭绕的小镇。", "旁白");
        await Ctrl.SayAsync("这就是传说中的{color=#FFD700}迷雾小镇{/color}。", "旁白");
        await Ctrl.SayAsync("（C# 场景内对话不创建逐句检查点，滚轮回溯将回到进入此场景前的状态。）",
            "系统", textColor: "#888888");

        // 提供导航按钮：进入 DSL 场景
        AddButton("进入小镇（DSL 场景）", 200, 500, 280, 44,
            nav: "town_entrance", color: "#88CCFF", halign: "center");

        // 清除对话状态，让按钮可交互
        Ctrl.Set(StateKeys.Dialog.Text, "");
        Ctrl.Set(StateKeys.Dialog.Complete, false);
        Ctrl.Set(StateKeys.Dialog.WaitingSayComplete, false);
    }
}
