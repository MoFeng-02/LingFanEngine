﻿using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Games;


/// <summary>
/// 剧情脚本基类 — 继承此类编写游戏剧情
/// </summary>
public abstract class StoryScript
{
    protected IStateContainer? _state;
    protected ICommandPipeline? _pipeline;
    protected ISceneRegistry? _sceneRegistry;

    /// <summary>场景标识名</summary>
    public abstract string SceneName { get; }
    public virtual SceneType SceneType => SceneType.Game;
    protected IGameController Ctrl { get; private set; } = null!;

    public void Initialize(IGameController ctrl, IStateContainer state,
        ICommandPipeline pipeline, ISceneRegistry sceneRegistry)
    {
        Ctrl = ctrl;
        _state = state;
        _pipeline = pipeline;
        _sceneRegistry = sceneRegistry;
    }

    public abstract Task Run();

    /// <summary>
    /// 属性定义，场景可独有，最终执行到这个场景的时候会将其纳入全局define
    /// </summary>
    /// <returns></returns>
    public virtual Dictionary<string, object?> InDefines() => [];

    /// <summary>设置场景背景 + 标题（清空之前所有元素）</summary>
    protected void SetScene(string backgroundPath, string? title = null,
        double bgOpacity = 0.4, int titleFontSize = 36, string titleColor = "#FFD700", int order = -2)
    {
        var elements = new List<UIElementEntity>
        {
            HelperImg(backgroundPath, 0, 0, null, null, bgOpacity, order, "background")
        };
        if (!string.IsNullOrEmpty(title))
            elements.Add(HelperTxt(title!, 0, 60, titleFontSize, titleColor, "center"));
        _state!.Set(StateKeys.Scene.Elements, elements);
        _state.Set(StateKeys.Scene.CurrentName, SceneName);
        _state.Set(StateKeys.Scene.Dirty, true);
    }

    protected void AddButton(string label, double x, double y, double w, double h,
        string? nav = null, string? cmd = null,
        string color = "#88CCFF", string? hoverColor = null,
        string halign = "left", string valign = "top")
    {
        AddElement(HelperBtn(label, x, y, w, h, nav, cmd, color, hoverColor, halign, valign));
    }

    protected void AddMenu(string prompt, params (string label, string target)[] options)
    {
        if (!string.IsNullOrEmpty(prompt))
            AddElement(HelperTxt(prompt, 0, 220, 22, "#FFD700", "center"));
        var startY = 280;
        for (int i = 0; i < options.Length; i++)
        {
            var isNav = !options[i].target.StartsWith("do_");
            AddButton(options[i].label, 0, startY + i * 60, 360, 44,
                isNav ? options[i].target : null,
                isNav ? null : options[i].target,
                nextColor(i), halign: "center");
        }
        _state!.Set(StateKeys.Dialog.Text, "");
        _state.Set(StateKeys.Dialog.Complete, false);
        _state.Set(StateKeys.Dialog.WaitingSayComplete, false);
    }

    protected void AddImage(string source, double x, double y,
        object? w = null, object? h = null, double opacity = 1.0,
        string halign = "left", string valign = "top", int order = 0)
    {
        AddElement(HelperImg(source, x, y, w, h, opacity, order, "image"));
    }

    protected void AddText(string text, double x, double y,
        double fontSize = 16, string color = "#FFFFFF",
        string halign = "left", string font = "Microsoft YaHei")
    {
        AddElement(HelperTxt(text, x, y, fontSize, color, halign, font));
    }

    protected void AddElement(UIElementEntity element)
    {
        var elements = _state!.Get<List<UIElementEntity>>(StateKeys.Scene.Elements) ?? [];
        var newList = new List<UIElementEntity>(elements) { element };
        _state.Set(StateKeys.Scene.Elements, newList);
        _state.Set(StateKeys.Scene.Dirty, true);
    }

    // ========== 颜色辅助 ==========

    private static readonly string[] s_menuColors = ["#88CCFF", "#88FF88", "#FFAA88", "#FF88AA", "#AA88FF", "#88FFCC"];
    private static string nextColor(int index) => s_menuColors[index % s_menuColors.Length];

    // ========== Helper 工厂 ==========

    private static UIElementEntity HelperTxt(string text, double x, double y,
        double fontSize, string color, string halign,
        string font = "Microsoft YaHei", int order = 0)
    {
        return new UIElementEntity
        {
            ElementType = "text",
            Properties = new Dictionary<string, object>
            {
                ["text"] = text,
                ["x"] = x,
                ["y"] = y,
                ["fontSize"] = fontSize,
                ["color"] = color,
                ["halign"] = halign,
                ["valign"] = "top",
                ["fontFamily"] = font
            },
            Order = order
        };
    }

    private static UIElementEntity HelperBtn(string text, double x, double y,
        double w, double h, string? nav, string? cmd,
        string color, string? hoverColor,
        string halign = "left", string valign = "top", int order = 0)
    {
        var props = new Dictionary<string, object>
        {
            ["text"] = text,
            ["x"] = x,
            ["y"] = y,
            ["width"] = w,
            ["height"] = h,
            ["color"] = color,
            ["halign"] = halign,
            ["valign"] = valign
        };
        if (nav != null) props["nav"] = nav;
        if (cmd != null) props["cmd"] = cmd;
        if (hoverColor != null) props["hover_color"] = hoverColor;
        return new UIElementEntity
        {
            ElementType = "button",
            Properties = props,
            Order = order,
            Command = cmd ?? nav,
            CommandValue = null
        };
    }

    private static UIElementEntity HelperImg(string source, double x, double y,
        object? w, object? h, double opacity, int order,
        string elementType = "image")
    {
        var props = new Dictionary<string, object>
        {
            ["source"] = source,
            ["x"] = x,
            ["y"] = y
        };
        if (w != null) props["width"] = w;
        if (h != null) props["height"] = h;
        if (opacity < 1.0) props["opacity"] = opacity;
        return new UIElementEntity { ElementType = elementType, Properties = props, Order = order };
    }
}