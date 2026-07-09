using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 设置变量命令处理器
/// <para>支持 define...once 语法（仅键不存在时设置）和表达式占位符求值。</para>
/// </summary>
public class SetVariableHandler : ICommandHandler<SetVariableCommand>, IDefaultCommandHandler
{
    public void Handle(SetVariableCommand sv, ICommandContext ctx)
    {
        // define ... once：只在键不存在时设置
        if (sv.IsDefine && ctx.State.ContainsKey(sv.Key))
            return;

        // 处理 DslExpressionPlaceholder（运行时求值）
        if (sv.Value is DslExpressionPlaceholder placeholder)
        {
            var result = DslExpressionEvaluator.Evaluate(placeholder.Expression, ctx.State);
            ctx.State.Set(sv.Key, result);
        }
        else
        {
            ctx.State.Set(sv.Key, sv.Value);
        }
    }
}

/// <summary>
/// 显示对话命令处理器
/// <para>替换 {变量} 表达式，设置对话文本、说话者、样式等状态。</para>
/// </summary>
public class ShowDialogHandler : ICommandHandler<ShowDialogCommand>, IDefaultCommandHandler
{
    public void Handle(ShowDialogCommand sd, ICommandContext ctx)
    {
        var dialogText = sd.Text;
        // 替换 {变量} 表达式
        if (dialogText.Contains('{'))
            dialogText = DslExpressionEvaluator.ReplaceText(dialogText, ctx.State);

        // 替换 speaker 中的 {变量} 表达式（如 speaker="{npc.innkeeper.name}"）
        var speakerName = sd.Speaker;
        if (!string.IsNullOrEmpty(speakerName) && speakerName.Contains('{'))
            speakerName = DslExpressionEvaluator.ReplaceText(speakerName, ctx.State);

        // NVL 模式：累积文本而非替换
        var nvlActive = ctx.State.Get<bool>(StateKeys.Nvl.Active);
        if (nvlActive)
        {
            var nvlText = ctx.State.Get<string>(StateKeys.Nvl.Text) ?? "";
            var nvlSpeakers = ctx.State.Get<string>(StateKeys.Nvl.Speakers) ?? "";
            var nvlCount = ctx.State.Get<int>(StateKeys.Nvl.Count);

            // 累积说话者（带换行，使用已求值的 speakerName）
            var speakerLine = speakerName ?? "";
            if (!string.IsNullOrEmpty(nvlSpeakers))
                nvlSpeakers += "\n";
            nvlSpeakers += speakerLine;

            // 累积文本（带换行）
            if (!string.IsNullOrEmpty(nvlText))
                nvlText += "\n";
            nvlText += dialogText;

            ctx.State.Set(StateKeys.Nvl.Text, nvlText);
            ctx.State.Set(StateKeys.Nvl.Speakers, nvlSpeakers);
            ctx.State.Set(StateKeys.Nvl.Count, nvlCount + 1);

            // NVL 模式下也设置常规对话状态（但用累积文本）
            ctx.State.Set(StateKeys.Dialog.Text, nvlText);
            ctx.State.Set(StateKeys.Dialog.Speaker, speakerName ?? "");
        }
        else
        {
            ctx.State.Set(StateKeys.Dialog.Text, dialogText);
            ctx.State.Set(StateKeys.Dialog.Speaker, speakerName ?? "");
        }
        // 角色样式——先查 character 定义，say 显式参数覆盖
        Dictionary<string, object?>? charDef = null;
        if (!string.IsNullOrEmpty(speakerName))
        {
            charDef = ctx.State.Get<Dictionary<string, object?>>(StateKeys.Characters.Prefix + speakerName);
        }

        // 角色定义中的 name 可覆盖说话者显示名
        if (charDef != null && charDef.TryGetValue("name", out var charName) && charName is string cn && !string.IsNullOrEmpty(cn))
        {
            speakerName = cn;
            // NVL 模式下也需要更新
            if (nvlActive)
            {
                // 修正 NVL 累积中的最后一行说话者
                var nvlSpeakers = ctx.State.Get<string>(StateKeys.Nvl.Speakers) ?? "";
                var lines = nvlSpeakers.Split('\n');
                if (lines.Length > 0) lines[^1] = cn;
                ctx.State.Set(StateKeys.Nvl.Speakers, string.Join("\n", lines));
            }
            else
            {
                ctx.State.Set(StateKeys.Dialog.Speaker, speakerName);
            }
        }

        ctx.State.Set(StateKeys.Dialog.SpeakerColor, sd.SpeakerColor ?? GetCharProp(charDef, "color"));
        ctx.State.Set(StateKeys.Dialog.TextColor, sd.TextColor ?? GetCharProp(charDef, "textcolor"));
        ctx.State.Set(StateKeys.Dialog.SpeakerFont, sd.SpeakerFont ?? GetCharProp(charDef, "font"));
        ctx.State.Set(StateKeys.Dialog.TextFont, sd.TextFont ?? GetCharProp(charDef, "textfont"));
        ctx.State.Set(StateKeys.Dialog.TypewriterEnabled, sd.TypewriterEnabled);
        // Phase 24: 设置侧脸图——优先使用 say 命令显式参数，其次角色定义的 side 属性
        ctx.State.Set<object?>(StateKeys.Dialog.SideImage, sd.SideImage ?? GetCharProp(charDef, "side"));
        // 对话栏尺寸（单句值优先，null=用全局默认）
        ctx.State.Set(StateKeys.Dialog.WidthPercent, sd.DialogPercentW);
        ctx.State.Set(StateKeys.Dialog.HeightPercent, sd.DialogPercentH);
        ctx.State.Set(StateKeys.Dialog.MarginLeft, sd.DialogMarginL);
        ctx.State.Set(StateKeys.Dialog.MarginBottom, sd.DialogMarginB);
        // 重置 dialog_complete 防止上一个对话的标记跳过这一句
        ctx.State.Set(StateKeys.Dialog.Complete, false);

        // 设置模态遮罩开关（say clickable=true / say okey 时按钮可点击）
        ctx.State.Set(StateKeys.Dialog.Clickable, sd.Clickable);

        // 记录对话历史（对标 Ren'Py _history_list）
        RecordHistory(sd, dialogText, speakerName, ctx);
    }

    /// <summary>
    /// 从角色定义字典中获取字符串属性
    /// </summary>
    private static string? GetCharProp(Dictionary<string, object?>? charDef, string key)
    {
        if (charDef == null) return null;
        return charDef.TryGetValue(key, out var val) && val is string s ? s : null;
    }

    /// <summary>
    /// 将当前对话追加到历史列表，超出上限时移除最旧条目
    /// <para>回溯重展示（Rollback/Rollforward）时不记录历史，避免重复。</para>
    /// </summary>
    private static void RecordHistory(ShowDialogCommand sd, string dialogText, string? speakerName, ICommandContext ctx)
    {
        // 回溯重展示时不记录历史
        if (ctx.State.Get<bool>(StateKeys.Rollback.IsReplay)) return;

        var history = ctx.State.Get<List<DialogHistoryEntry>>(StateKeys.History.Entries) ?? [];
        var maxCount = ctx.State.Get<int>(StateKeys.History.MaxCount);
        if (maxCount <= 0) maxCount = 100;

        history.Add(new DialogHistoryEntry
        {
            Speaker = speakerName,
            Text = dialogText,
            SceneName = ctx.State.Get<string>(StateKeys.Scene.CurrentName),
            CheckpointIndex = ctx.State.Get<int>(StateKeys.Rollback.CurrentIndex)
        });

        // 超出上限时移除最旧条目
        while (history.Count > maxCount)
            history.RemoveAt(0);

        ctx.State.Set(StateKeys.History.Entries, history);
    }
}

/// <summary>
/// 追加对话命令处理器（对标 Ren'Py extend）
/// </summary>
public class ExtendDialogHandler : ICommandHandler<ExtendDialogCommand>, IDefaultCommandHandler
{
    public void Handle(ExtendDialogCommand ed, ICommandContext ctx)
    {
        var cur = ctx.State.Get<string>(StateKeys.Dialog.Text) ?? "";
        ctx.State.Set(StateKeys.Dialog.Text, cur + ed.Append);
        ctx.State.Set(StateKeys.Dialog.Complete, false);
    }
}
