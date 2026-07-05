using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 过渡动画命令处理器
/// <para>启动过渡动画，设置过渡状态到状态容器。</para>
/// </summary>
public class TransitionHandler : ICommandHandler<TransitionCommand>
{
    public void Handle(TransitionCommand tc, ICommandContext ctx)
    {
        ctx.State.Set(StateKeys.Transition.Active, true);
        ctx.State.Set(StateKeys.Transition.Type, tc.Type);
        ctx.State.Set(StateKeys.Transition.Progress, 0.0);
        ctx.State.Set(StateKeys.Transition.Duration, tc.Duration);
        ctx.State.Set(StateKeys.Transition.OffsetX, 0.0);
        ctx.State.Set(StateKeys.Transition.OffsetY, 0.0);
        ctx.State.Set(StateKeys.Transition.Scale, 1.0);
        ctx.State.Set(StateKeys.Transition.Elapsed, 0.0);
        ctx.State.Set(StateKeys.Transition.Easing, "EaseOutQuad");
    }
}

/// <summary>
/// 控件级动画命令处理器
/// <para>存起始值、目标值、进度到状态容器，SceneView 每帧读取动画状态更新控件。</para>
/// </summary>
public class AnimateHandler : ICommandHandler<AnimateCommand>
{
    public void Handle(AnimateCommand ac, ICommandContext ctx)
    {
        var animKey = $"{StateKeys.Animation.Prefix}{ac.Target}_{ac.Property}";
        var curVal = ctx.State.Get<double>(animKey + StateKeys.Animation.CurrentSuffix);
        ctx.State.Set(animKey + StateKeys.Animation.FromSuffix, curVal);
        ctx.State.Set(animKey + StateKeys.Animation.TargetSuffix, ac.TargetValue);
        ctx.State.Set(animKey + StateKeys.Animation.DurationSuffix, ac.Duration);
        ctx.State.Set(animKey + StateKeys.Animation.EasingSuffix, ac.Easing);
        ctx.State.Set(animKey + StateKeys.Animation.ElapsedSuffix, 0.0);
        ctx.State.Set(animKey + StateKeys.Animation.ActiveSuffix, true);
        ctx.State.Set(animKey + StateKeys.Animation.RepeatSuffix, ac.RepeatCount);
        // 初始值（如果不存在的话）
        if (!ctx.State.ContainsKey(animKey + StateKeys.Animation.CurrentSuffix))
            ctx.State.Set(animKey + StateKeys.Animation.CurrentSuffix, 0.0);
    }
}

/// <summary>
/// show/hide/background 命令处理器
/// <para>操作运行时元素层（对标 Ren'Py），不改变场景定义。</para>
/// </summary>
public class ShowHideHandler : ICommandHandler<ShowHideCommand>
{
    public void Handle(ShowHideCommand sh, ICommandContext ctx)
    {
        var rtElements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements) ?? [];
        var rtList = new List<UIElementEntity>(rtElements);

        if (sh.IsShow)
        {
            var elType = sh.IsBackground ? "background" : "image";
            var props = new Dictionary<string, object>
            {
                ["source"] = sh.Target,
                ["x"] = sh.X,
                ["y"] = sh.Y,
                [StateKeys.UiTags.Tag] = sh.Tag ?? ""
            };
            // background 始终在底层（Order = -1000）
            rtList.Add(new UIElementEntity
            {
                ElementType = elType,
                Properties = props,
                Order = sh.IsBackground ? -1000 : rtElements.Count
            });
        }
        else
        {
            // hide：移除所有匹配的元素（按 source 或 tag 匹配）
            rtList.RemoveAll(e =>
                (e.Properties.TryGetValue("source", out var src) &&
                 src?.ToString() == sh.Target) ||
                (!string.IsNullOrEmpty(sh.Tag) &&
                 e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                 t?.ToString() == sh.Tag));
        }

        ctx.State.Set(StateKeys.Scene.RuntimeElements, rtList);
        // background 同时更新场景的背景引用（供存档/预览用）
        if (sh.IsBackground)
            ctx.State.Set(StateKeys.Scene.CurrentBackground, sh.Target);
    }
}
