using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 设置变量命令处理器
/// <para>支持 define...once 语法（仅键不存在时设置）和表达式占位符求值。</para>
/// </summary>
public class SetVariableHandler : ICommandHandler<SetVariableCommand>
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
public class ShowDialogHandler : ICommandHandler<ShowDialogCommand>
{
    public void Handle(ShowDialogCommand sd, ICommandContext ctx)
    {
        var dialogText = sd.Text;
        // 替换 {变量} 表达式
        if (dialogText.Contains('{'))
            dialogText = DslExpressionEvaluator.ReplaceText(dialogText, ctx.State);

        // NVL 模式：累积文本而非替换
        var nvlActive = ctx.State.Get<bool>(StateKeys.Nvl.Active);
        if (nvlActive)
        {
            var nvlText = ctx.State.Get<string>(StateKeys.Nvl.Text) ?? "";
            var nvlSpeakers = ctx.State.Get<string>(StateKeys.Nvl.Speakers) ?? "";
            var nvlCount = ctx.State.Get<int>(StateKeys.Nvl.Count);

            // 累积说话者（带换行）
            var speakerLine = sd.Speaker ?? "";
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
            ctx.State.Set(StateKeys.Dialog.Speaker, sd.Speaker ?? "");
        }
        else
        {
            ctx.State.Set(StateKeys.Dialog.Text, dialogText);
            ctx.State.Set(StateKeys.Dialog.Speaker, sd.Speaker ?? "");
        }
        // 角色样式（对标 Ren'Py Character 对象）
        ctx.State.Set(StateKeys.Dialog.SpeakerColor, sd.SpeakerColor ?? (string?)null);
        ctx.State.Set(StateKeys.Dialog.TextColor, sd.TextColor ?? (string?)null);
        ctx.State.Set(StateKeys.Dialog.SpeakerFont, sd.SpeakerFont ?? (string?)null);
        ctx.State.Set(StateKeys.Dialog.TextFont, sd.TextFont ?? (string?)null);
        ctx.State.Set(StateKeys.Dialog.TypewriterEnabled, sd.TypewriterEnabled);
        // 对话栏尺寸（单句值优先，null=用全局默认）
        ctx.State.Set(StateKeys.Dialog.WidthPercent, sd.DialogPercentW);
        ctx.State.Set(StateKeys.Dialog.HeightPercent, sd.DialogPercentH);
        ctx.State.Set(StateKeys.Dialog.MarginLeft, sd.DialogMarginL);
        ctx.State.Set(StateKeys.Dialog.MarginBottom, sd.DialogMarginB);
        // 重置 dialog_complete 防止上一个对话的标记跳过这一句
        ctx.State.Set(StateKeys.Dialog.Complete, false);

        // 记录对话历史（对标 Ren'Py _history_list）
        RecordHistory(sd, dialogText, ctx);
    }

    /// <summary>
    /// 将当前对话追加到历史列表，超出上限时移除最旧条目
    /// </summary>
    private static void RecordHistory(ShowDialogCommand sd, string dialogText, ICommandContext ctx)
    {
        var history = ctx.State.Get<List<DialogHistoryEntry>>(StateKeys.History.Entries) ?? [];
        var maxCount = ctx.State.Get<int>(StateKeys.History.MaxCount);
        if (maxCount <= 0) maxCount = 100;

        history.Add(new DialogHistoryEntry
        {
            Speaker = sd.Speaker,
            Text = dialogText,
            SceneName = ctx.State.Get<string>(StateKeys.Scene.CurrentName)
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
public class ExtendDialogHandler : ICommandHandler<ExtendDialogCommand>
{
    public void Handle(ExtendDialogCommand ed, ICommandContext ctx)
    {
        var cur = ctx.State.Get<string>(StateKeys.Dialog.Text) ?? "";
        ctx.State.Set(StateKeys.Dialog.Text, cur + ed.Append);
        ctx.State.Set(StateKeys.Dialog.Complete, false);
    }
}
