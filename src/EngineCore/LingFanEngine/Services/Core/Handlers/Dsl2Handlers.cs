using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

// ====== Phase 44: 叙事增强（array/dict） ======

/// <summary>
/// 数组追加命令处理器——DSL 2.0
/// <para>array_push "key" "value" → 将值追加到状态容器中的 List。</para>
/// <para>如果键不存在或不是 List，则创建新 List。</para>
/// </summary>
public class ArrayPushHandler : ICommandHandler<ArrayPushCommand>, IDefaultCommandHandler
{
    public void Handle(ArrayPushCommand cmd, ICommandContext ctx)
    {
        var value = DslExpressionEvaluator.Evaluate(cmd.ValuePart, ctx.State);

        var existing = ctx.State.Get<List<object?>>(cmd.Key) ?? [];
        // 创建新列表（防止回溯快照引用污染）
        var newList = new List<object?>(existing) { value };
        ctx.State.Set(cmd.Key, newList);
    }
}

/// <summary>
/// 数组弹出命令处理器——DSL 2.0
/// <para>array_pop "key" → 移除并返回列表的最后一个元素。</para>
/// </summary>
public class ArrayPopHandler : ICommandHandler<ArrayPopCommand>, IDefaultCommandHandler
{
    public void Handle(ArrayPopCommand cmd, ICommandContext ctx)
    {
        var existing = ctx.State.Get<List<object?>>(cmd.Key);
        if (existing == null || existing.Count == 0)
            return;

        var last = existing[^1];
        // 创建新列表（防止回溯快照引用污染）
        var newList = new List<object?>(existing);
        newList.RemoveAt(newList.Count - 1);

        // 弹出的值存入 _popped 临时键（供表达式引用）
        ctx.State.Set(cmd.Key + "_popped", last);
        ctx.State.Set(cmd.Key, newList);
    }
}

/// <summary>
/// 字典设值命令处理器——DSL 2.0
/// <para>dict_set "key" "field" "value" → 在状态容器的字典中设置字段值。</para>
/// <para>如果字典不存在则创建新字典。</para>
/// </summary>
public class DictSetHandler : ICommandHandler<DictSetCommand>, IDefaultCommandHandler
{
    public void Handle(DictSetCommand cmd, ICommandContext ctx)
    {
        var value = DslExpressionEvaluator.Evaluate(cmd.ValuePart, ctx.State);

        var existing = ctx.State.Get<Dictionary<string, object?>>(cmd.Key) ?? [];
        // 创建新字典（防止回溯快照引用污染）
        var newDict = new Dictionary<string, object?>(existing) { [cmd.Field] = value };
        ctx.State.Set(cmd.Key, newDict);
    }
}

// ====== Phase 45: UI 增强（sprite/bg_switch） ======

/// <summary>
/// 立绘操作命令处理器——DSL 2.0
/// <para>统一处理 sprite show/state/move/hide 操作。</para>
/// <para>sprite 以运行时元素形式存在，通过 Tag 匹配定位。</para>
/// </summary>
public class SpriteHandler : ICommandHandler<SpriteCommand>, IDefaultCommandHandler
{
    public void Handle(SpriteCommand cmd, ICommandContext ctx)
    {
        var rtElements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements) ?? [];
        var rtList = new List<UIElementEntity>(rtElements);

        switch (cmd.Operation)
        {
            case "show":
                // 移除同 ID 的旧立绘（替换）
                rtList.RemoveAll(e =>
                    e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                    t?.ToString() == cmd.Id);

                var props = new Dictionary<string, object>
                {
                    ["source"] = cmd.Source ?? "",
                    ["x"] = cmd.X ?? 0,
                    ["y"] = cmd.Y ?? 0,
                    [StateKeys.UiTags.Tag] = cmd.Id
                };
                rtList.Add(new UIElementEntity
                {
                    ElementType = "image",
                    Properties = props,
                    Order = rtElements.Count
                });
                break;

            case "state":
                // 切换立绘表情：重建匹配元素（避免修改共享引用）
                for (int i = 0; i < rtList.Count; i++)
                {
                    var e = rtList[i];
                    if (e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                        t?.ToString() == cmd.Id)
                    {
                        var newProps = new Dictionary<string, object>(e.Properties);
                        if (!string.IsNullOrEmpty(cmd.Emotion))
                            newProps["source"] = cmd.Emotion;
                        rtList[i] = new UIElementEntity
                        {
                            Id = e.Id,
                            ElementType = e.ElementType,
                            InCustom = e.InCustom,
                            CustomElement = e.CustomElement,
                            Properties = newProps,
                            Children = e.Children,
                            Order = e.Order,
                            Command = e.Command,
                            CommandValue = e.CommandValue
                        };
                    }
                }
                break;

            case "move":
                // 移动立绘：重建匹配元素（避免修改共享引用）
                for (int i = 0; i < rtList.Count; i++)
                {
                    var e = rtList[i];
                    if (e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                        t?.ToString() == cmd.Id)
                    {
                        var newProps = new Dictionary<string, object>(e.Properties);
                        if (cmd.X.HasValue) newProps["x"] = cmd.X.Value;
                        if (cmd.Y.HasValue) newProps["y"] = cmd.Y.Value;
                        rtList[i] = new UIElementEntity
                        {
                            Id = e.Id,
                            ElementType = e.ElementType,
                            InCustom = e.InCustom,
                            CustomElement = e.CustomElement,
                            Properties = newProps,
                            Children = e.Children,
                            Order = e.Order,
                            Command = e.Command,
                            CommandValue = e.CommandValue
                        };
                    }
                }
                break;

            case "hide":
                // 移除指定 ID 的立绘
                rtList.RemoveAll(e =>
                    e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                    t?.ToString() == cmd.Id);
                break;
        }

        ctx.State.Set(StateKeys.Scene.RuntimeElements, rtList);
    }
}

/// <summary>
/// 背景切换命令处理器——DSL 2.0
/// <para>bg_switch "path" transition=fade duration=N → 切换背景，可选过渡动画。</para>
/// </summary>
public class BgSwitchHandler : ICommandHandler<BgSwitchCommand>, IDefaultCommandHandler
{
    public void Handle(BgSwitchCommand cmd, ICommandContext ctx)
    {
        // 如果指定了过渡，设置过渡状态键（与 TransitionHandler 一致）
        if (!string.IsNullOrEmpty(cmd.Transition))
        {
            var duration = cmd.Duration ?? 0.5;
            ctx.State.Set(StateKeys.Transition.Active, true);
            ctx.State.Set(StateKeys.Transition.Type, cmd.Transition);
            ctx.State.Set(StateKeys.Transition.Progress, 0.0);
            ctx.State.Set(StateKeys.Transition.Duration, duration);
            ctx.State.Set(StateKeys.Transition.Elapsed, 0.0);
            ctx.State.Set(StateKeys.Transition.OffsetX, 0.0);
            ctx.State.Set(StateKeys.Transition.OffsetY, 0.0);
            ctx.State.Set(StateKeys.Transition.Scale, 1.0);
            ctx.State.Set(StateKeys.Transition.Easing, "EaseOutQuad");
        }

        // 移除旧背景元素
        var rtElements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements) ?? [];
        var rtList = new List<UIElementEntity>(rtElements);
        rtList.RemoveAll(e => e.ElementType == "background");
        rtList.Add(new UIElementEntity
        {
            ElementType = "background",
            Properties = new Dictionary<string, object>
            {
                ["source"] = cmd.Path,
                ["x"] = 0.0,
                ["y"] = 0.0,
                [StateKeys.UiTags.Tag] = "background"
            },
            Order = -1000
        });
        ctx.State.Set(StateKeys.Scene.RuntimeElements, rtList);
        ctx.State.Set(StateKeys.Scene.CurrentBackground, cmd.Path);
    }
}

// ====== Phase 46: Live2D ======

/// <summary>
/// Live2D 命令处理器——DSL 2.0
/// <para>统一处理 live2d char/show/motion/expr/param/hide/pause/resume 操作。</para>
/// <para>状态驱动模式：写入 __live2d_* 状态键，SceneView 读取后驱动 Live2D 控件。</para>
/// </summary>
public class Live2DHandler : ICommandHandler<Live2DCommand>, IDefaultCommandHandler
{
    public void Handle(Live2DCommand cmd, ICommandContext ctx)
    {
        switch (cmd.Operation)
        {
            case "char":
                // 注册模型配置（类似 character 定义）
                if (cmd.Config != null)
                    ctx.State.Set(StateKeys.Live2D.CharPrefix + cmd.Id, cmd.Config);
                break;

            case "show":
                // 添加 Live2D 运行时元素
                var rtElements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements) ?? [];
                var rtList = new List<UIElementEntity>(rtElements);

                // 移除同 ID 的旧 Live2D 元素
                rtList.RemoveAll(e =>
                    e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                    t?.ToString() == cmd.Id);

                rtList.Add(new UIElementEntity
                {
                    ElementType = "Live2D",
                    Properties = new Dictionary<string, object>
                    {
                        ["modelId"] = cmd.Id,
                        [StateKeys.UiTags.Tag] = cmd.Id
                    },
                    Order = rtElements.Count
                });
                ctx.State.Set(StateKeys.Scene.RuntimeElements, rtList);
                break;

            case "motion":
                ctx.State.Set(
                    StateKeys.Live2D.ModelPrefix + cmd.Id + StateKeys.Live2D.MotionSuffix,
                    new Dictionary<string, object?>
                    {
                        ["name"] = cmd.Name,
                        ["loop"] = cmd.Loop
                    });
                break;

            case "expr":
                ctx.State.Set(
                    StateKeys.Live2D.ModelPrefix + cmd.Id + StateKeys.Live2D.ExprSuffix,
                    cmd.Name);
                break;

            case "param":
                ctx.State.Set(
                    StateKeys.Live2D.ModelPrefix + cmd.Id + StateKeys.Live2D.ParamPrefix + cmd.ParamName,
                    new Dictionary<string, object?>
                    {
                        ["value"] = cmd.ParamValue,
                        ["weight"] = cmd.Weight
                    });
                break;

            case "hide":
                // 移除 Live2D 运行时元素
                var elements = ctx.State.Get<List<UIElementEntity>>(StateKeys.Scene.RuntimeElements) ?? [];
                var list = new List<UIElementEntity>(elements);
                list.RemoveAll(e =>
                    e.Properties.TryGetValue(StateKeys.UiTags.Tag, out var t) &&
                    t?.ToString() == cmd.Id);
                ctx.State.Set(StateKeys.Scene.RuntimeElements, list);
                break;

            case "pause":
                ctx.State.Set(
                    StateKeys.Live2D.ModelPrefix + cmd.Id + StateKeys.Live2D.PausedSuffix,
                    true);
                break;

            case "resume":
                ctx.State.Set(
                    StateKeys.Live2D.ModelPrefix + cmd.Id + StateKeys.Live2D.PausedSuffix,
                    false);
                break;
        }
    }
}

// ====== Phase 47: 存档/成就/章节 ======

/// <summary>
/// 成就解锁命令处理器——DSL 2.0
/// <para>将成就条目写入状态容器的已解锁列表，供成就界面读取。</para>
/// <para>幂等操作：已解锁的成就不会重复添加。</para>
/// </summary>
public class AchievementUnlockHandler : ICommandHandler<AchievementUnlockCommand>, IDefaultCommandHandler
{
    public void Handle(AchievementUnlockCommand cmd, ICommandContext ctx)
    {
        var existing = ctx.State.Get<List<AchievementEntry>>(StateKeys.Achievements.Unlocked) ?? [];

        // 已解锁则跳过（幂等）
        if (existing.Any(e => e.Id == cmd.Id))
            return;

        // 创建新列表（防止回溯快照引用污染）
        var newList = new List<AchievementEntry>(existing) { new() { Id = cmd.Id, Name = cmd.AchievementName } };

        ctx.State.Set(StateKeys.Achievements.Unlocked, newList);

        System.Diagnostics.Debug.WriteLine(
            $"[AchievementUnlockHandler] 成就解锁: {cmd.Id} ({cmd.AchievementName})");
    }
}

/// <summary>
/// 章节解锁命令处理器——DSL 2.0
/// <para>将章节条目写入状态容器的已解锁列表，供章节选择界面读取。</para>
/// <para>幂等操作：已存在的章节更新解锁状态而非重复添加。</para>
/// </summary>
public class ChapterUnlockHandler : ICommandHandler<ChapterUnlockCommand>, IDefaultCommandHandler
{
    public void Handle(ChapterUnlockCommand cmd, ICommandContext ctx)
    {
        var existing = ctx.State.Get<List<ChapterEntry>>(StateKeys.Chapters.Unlocked) ?? [];

        // 不可变更新模式：创建新列表 + 新 ChapterEntry 对象，防止回溯快照引用污染
        var newList = new List<ChapterEntry>(existing.Count);
        bool found = false;
        foreach (var entry in existing)
        {
            if (entry.Id == cmd.Id)
            {
                found = true;
                newList.Add(new ChapterEntry
                {
                    Id = entry.Id,
                    Name = cmd.ChapterName ?? entry.Name,
                    Unlocked = cmd.Unlock,
                    UnlockedAt = cmd.Unlock ? DateTimeOffset.UtcNow : entry.UnlockedAt
                });
            }
            else
            {
                newList.Add(entry);
            }
        }

        if (!found)
        {
            newList.Add(new ChapterEntry
            {
                Id = cmd.Id,
                Name = cmd.ChapterName,
                Unlocked = cmd.Unlock
            });
        }

        ctx.State.Set(StateKeys.Chapters.Unlocked, newList);

        System.Diagnostics.Debug.WriteLine(
            $"[ChapterUnlockHandler] 章节 {(cmd.Unlock ? "解锁" : "锁定")}: {cmd.Id} ({cmd.ChapterName})");
    }
}

/// <summary>
/// 删除存档命令处理器——DSL 2.0
/// <para>save_delete "slot" → 删除指定槽位的存档文件。</para>
/// </summary>
public class SaveDeleteHandler : ICommandHandler<SaveDeleteCommand>, IDefaultCommandHandler
{
    public void Handle(SaveDeleteCommand cmd, ICommandContext ctx)
    {
        if (ctx.SaveService == null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await ctx.SaveService.DeleteAsync(cmd.SlotId);
                System.Diagnostics.Debug.WriteLine(
                    $"[SaveDeleteHandler] 存档已删除: {cmd.SlotId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SaveDeleteHandler] 删除存档失败: {ex.Message}");
            }
        });
    }
}
