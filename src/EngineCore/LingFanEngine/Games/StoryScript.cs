using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.Enums;
using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using System.Threading;

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

    public abstract Task RunAsync();

    /// <summary>
    /// 属性定义，场景可独有，最终执行到这个场景的时候会将其纳入全局define
    /// </summary>
    /// <returns></returns>
    public virtual Dictionary<string, object?> InDefines() => [];

    /// <summary>
    /// 时间注册事件，场景所有
    /// </summary>
    /// <returns></returns>
    public virtual IReadOnlyList<TimeEventRegistration> InTimeEvents() => [];

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

    // ========== 剧情流便捷方法（对标 DSL，不改变上方任何成员） ==========
    // 目标：让 C# 端写故事也能像 DSL 一样顺手——顺序叙事 + 选择分支 + 时间事件。
    // 全部为对 Ctrl(IGameController) 的薄封装，AOT 友好（无反射）。

    /// <summary>说一句话（对标 DSL say）。</summary>
    protected Task SayAsync(string text, string? speaker = null, string? speakerColor = null,
        string? textColor = null, bool typewriter = true, string? voice = null,
        CancellationToken ct = default)
        => Ctrl.SayAsync(text, speaker, speakerColor, textColor, typewriter, voice: voice,
            ct: ct);

    /// <summary>追加文本到当前对话（对标 Ren'Py extend）。</summary>
    protected Task ExtendAsync(string append, CancellationToken ct = default)
        => Ctrl.ExtendDialogAsync(append, ct: ct);

    /// <summary>跳转到场景（对标 DSL navigate）。</summary>
    protected Task NavigateAsync(string sceneName) => Ctrl.NavigateAsync(sceneName);

    /// <summary>变量赋值（对标 DSL set）。</summary>
    protected void Set(string key, object? value) => Ctrl.Set(key, value);

    /// <summary>全局变量定义（对标 DSL define，全局初始化）。</summary>
    protected void Define(string key, object? value) => Ctrl.Define(key, value);

    /// <summary>定义角色对话样式（对标 DSL character）。</summary>
    protected void Character(string key, string? name = null, string? color = null,
        string? font = null, string? textColor = null, string? textFont = null, string? sideImage = null)
        => Ctrl.DefineCharacter(key, name, color, font, textColor, textFont, sideImage);

    /// <summary>弹出选择菜单，返回选中项索引（对标 DSL menu，0 基）。</summary>
    protected Task<int> ChoiceAsync(string prompt, CancellationToken ct = default, params string[] options)
        => Ctrl.ShowMenuAsync(prompt, options, ct: ct);

    /// <summary>
    /// 弹出选择菜单并执行对应回调（对标 DSL menu + 分支，免手写 switch）。
    /// <code>
    /// await ChoiceAsync("你要做什么？",
    ///     ("探索", () => NavigateAsync("explore")),
    ///     ("休息", async () => await SayAsync("你睡了一觉。")));
    /// </code>
    /// </summary>
    protected async Task ChoiceAsync(string prompt, CancellationToken ct = default, params (string Label, Func<Task> OnSelect)[] options)
    {
        var labels = new string[options.Length];
        for (var i = 0; i < options.Length; i++) labels[i] = options[i].Label;
        var idx = await Ctrl.ShowMenuAsync(prompt, labels, ct);
        if (idx >= 0 && idx < options.Length) await options[idx].OnSelect();
    }

    /// <summary>等待用户输入文本（对标 DSL input）。</summary>
    protected Task<string?> InputAsync(string prompt, string[]? options = null, CancellationToken ct = default)
        => Ctrl.InputAsync(prompt, options, ct: ct);

    /// <summary>等待指定秒数（不可跳过）。</summary>
    protected Task WaitAsync(double seconds, CancellationToken ct = default)
        => Ctrl.WaitAsync(seconds, ct: ct);

    /// <summary>等待用户点击（对标 DSL pause）。</summary>
    protected Task WaitClickAsync(CancellationToken ct = default)
        => Ctrl.WaitForClickAsync(ct: ct);

    /// <summary>可跳过的定时等待（对标 Ren'Py pause(delay)）。</summary>
    protected Task WaitSkipableAsync(double seconds, CancellationToken ct = default)
        => Ctrl.SkipableWaitAsync(seconds, ct: ct);

    /// <summary>场景过渡（对标 DSL transition）。</summary>
    protected Task TransitionAsync(string type, double duration = 0.5, CancellationToken ct = default)
        => Ctrl.TransitionAsync(type, duration, ct: ct);

    /// <summary>设置/切换背景图（对标 DSL background）。</summary>
    protected Task BackgroundAsync(string path) => Ctrl.BackgroundAsync(path);

    /// <summary>播放 BGM（对标 DSL bgm）。</summary>
    protected void Bgm(string path, float volume = 0.8f, double fadeIn = 0, bool? autoStop = null)
        => Ctrl.PlayBgm(path, volume, fadeIn, autoStop);

    /// <summary>停止 BGM。</summary>
    protected void StopBgm(double fadeOut = 0) => Ctrl.StopBgm(fadeOut);

    /// <summary>播放音效（对标 DSL se）。</summary>
    protected void Se(string path, float volume = 0.6f) => Ctrl.PlaySe(path, volume);

    /// <summary>停止音效。</summary>
    protected void StopSe() => Ctrl.StopSe();

    /// <summary>播放环境音（对标 DSL ambient）。</summary>
    protected void Ambient(string path, float volume = 0.8f, bool loop = true)
        => Ctrl.PlayAmbient(path, volume, loop);

    /// <summary>停止环境音。</summary>
    protected void StopAmbient() => Ctrl.StopAmbient();

    /// <summary>播放语音（对标 DSL voice）。</summary>
    protected void Voice(string path, float volume = 1.0f, bool? autoStop = null)
        => Ctrl.PlayVoice(path, volume, autoStop);

    /// <summary>停止语音。</summary>
    protected void StopVoice() => Ctrl.StopVoice();

    /// <summary>显示通知 Toast（对标 DSL notify）。</summary>
    protected void Notify(string text, string type = "info", double duration = 0)
        => Ctrl.Notify(text, type, duration);

    /// <summary>存档（对标 DSL save）。</summary>
    protected Task SaveAsync(string slot) => Ctrl.SaveAsync(slot);

    /// <summary>读档（对标 DSL load）。</summary>
    protected Task LoadAsync(string slot) => Ctrl.LoadAsync(slot);

    /// <summary>回溯一步（对标 DSL rollback）。</summary>
    protected Task RollbackAsync() => Ctrl.RollbackAsync();

    /// <summary>前进一步（对标 DSL rollforward）。</summary>
    protected Task RollforwardAsync() => Ctrl.RollforwardAsync();

    /// <summary>返回上一场景（对标 DSL back）。</summary>
    protected Task BackAsync() => Ctrl.BackAsync();

    /// <summary>前进到下一场景（对标 DSL forward）。</summary>
    protected Task ForwardAsync() => Ctrl.ForwardAsync();

    /// <summary>屏幕震动（对标 DSL shake）。</summary>
    protected Task ShakeAsync(double intensity = 10.0, double duration = 0.5)
        => Ctrl.ShakeAsync(intensity, duration);

    // ---- 时间事件（对标 DSL time_event / set_time_event / unregister_time_event / restore_time_event） ----

    /// <summary>注册时间事件（触发时导航到目标场景，对标 DSL time_event）。</summary>
    protected void TimeEvent(string target, int triggerDay, int? triggerHour = null,
        int? triggerMinute = null, bool isOneShot = true, string? condition = null, string? description = null)
        => Ctrl.RegisterTimeEvent(target, triggerDay, triggerHour, triggerMinute, isOneShot, condition, description);

    /// <summary>注册每日重复时间事件（对标 DSL time_event 每日）。</summary>
    protected void DailyEvent(string target, int triggerHour, int? triggerMinute = null,
        string? condition = null, string? description = null)
        => Ctrl.RegisterDailyEvent(target, triggerHour, triggerMinute, condition, description);

    /// <summary>注册按星期触发的时间事件（对标 DSL time_event 星期）。</summary>
    protected void WeeklyEvent(string target, DayOfWeek[] daysOfWeek, int triggerHour,
        int? triggerMinute = null, bool isOneShot = false, string? condition = null, string? description = null)
        => Ctrl.RegisterWeeklyEvent(target, daysOfWeek, triggerHour, triggerMinute, isOneShot, condition, description);

    /// <summary>注册回调驱动的时间事件（时间到执行 callback，对标 DSL set_time_event）。</summary>
    protected void CallbackEvent(string id, int hour, Func<Task> callback, bool once = false,
        HashSet<DayOfWeek>? weekdays = null, int? minute = null, int? day = null)
        => Ctrl.SetTimeEventAsync(id, hour, callback, once, weekdays, minute, day);

    /// <summary>注销时间事件（对标 DSL unregister_time_event）。</summary>
    protected void UnregisterEvent(string id, bool permanent = false, bool temporary = false)
        => Ctrl.UnregisterEvent(id, permanent, temporary);

    /// <summary>恢复已注销的时间事件（对标 DSL restore_time_event）。</summary>
    protected void RestoreEvent(string id) => Ctrl.RestoreEvent(id);

    /// <summary>暂停游戏时间推进。</summary>
    protected void PauseTime() => Ctrl.PauseGameTime();

    /// <summary>恢复游戏时间推进。</summary>
    protected void ResumeTime() => Ctrl.ResumeGameTime();

    /// <summary>批量跳过游戏时间（逐分钟 Tick，确保中间时间事件被检查）。</summary>
    protected void SkipTime(int minutes) => Ctrl.SkipTime(minutes);

    /// <summary>重置游戏时间并清空所有已注册时间事件。</summary>
    protected void ResetTime() => Ctrl.ResetGameTime();

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
                ["font"] = font
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