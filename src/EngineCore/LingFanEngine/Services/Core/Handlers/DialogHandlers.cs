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
        // 原文翻译（I18n）——在变量替换之前，翻译 key 是带 {占位符} 的原文
        if (ctx.I18n != null && !string.IsNullOrEmpty(dialogText))
            dialogText = ctx.I18n.Translate(dialogText);
        // 替换 {变量} 表达式
        if (dialogText.Contains('{'))
            dialogText = DslExpressionEvaluator.ReplaceText(dialogText, ctx.State);

        // 替换 speaker 中的 {变量} 表达式（如 speaker="{npc.innkeeper.name}"）
        var speakerKey = sd.Speaker;
        if (!string.IsNullOrEmpty(speakerKey) && speakerKey.Contains('{'))
            speakerKey = DslExpressionEvaluator.ReplaceText(speakerKey, ctx.State);

        // 1. 先查角色定义，解析显示名和样式
        Dictionary<string, object?>? charDef = null;
        if (!string.IsNullOrEmpty(speakerKey))
        {
            charDef = ctx.State.Get<Dictionary<string, object?>>(StateKeys.Characters.Prefix + speakerKey);
        }

        // 角色定义中的 name 覆盖说话者显示名
        var speakerName = speakerKey;
        if (charDef != null && charDef.TryGetValue("name", out var charName) && charName is string cn && !string.IsNullOrEmpty(cn))
        {
            speakerName = cn;
        }

        // 2. NVL 模式：累积文本（含说话者名称内联）而非替换
        var nvlActive = ctx.State.Get<bool>(StateKeys.Nvl.Active);
        if (nvlActive)
        {
            var nvlText = ctx.State.Get<string>(StateKeys.Nvl.Text) ?? "";
            var nvlSpeakers = ctx.State.Get<string>(StateKeys.Nvl.Speakers) ?? "";
            var nvlCount = ctx.State.Get<int>(StateKeys.Nvl.Count);

            // 累积说话者（带换行）
            if (!string.IsNullOrEmpty(nvlSpeakers))
                nvlSpeakers += "\n";
            nvlSpeakers += speakerName ?? "";

            // 累积显示文本——说话者名称内联（对标 Ren'Py NVL 默认布局）
            // 格式："说话者：对话文本" 或 "对话文本"（无说话者时）
            var displayLine = string.IsNullOrEmpty(speakerName)
                ? dialogText
                : $"{speakerName}：{dialogText}";

            if (!string.IsNullOrEmpty(nvlText))
                nvlText += "\n";
            nvlText += displayLine;

            ctx.State.Set(StateKeys.Nvl.Text, nvlText);
            ctx.State.Set(StateKeys.Nvl.Speakers, nvlSpeakers);
            ctx.State.Set(StateKeys.Nvl.Count, nvlCount + 1);

            // NVL 模式下也设置常规对话状态（用累积文本）
            ctx.State.Set(StateKeys.Dialog.Text, nvlText);
            ctx.State.Set(StateKeys.Dialog.Speaker, speakerName ?? "");
        }
        else
        {
            ctx.State.Set(StateKeys.Dialog.Text, dialogText);
            ctx.State.Set(StateKeys.Dialog.Speaker, speakerName ?? "");
        }

        // 3. 设置角色样式（say 显式参数覆盖角色定义）
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

        // Phase 37: 设置 noskip 标记（say noskip=true 时 Skip 模式下仍需手动点击）
        ctx.State.Set(StateKeys.Dialog.Noskip, sd.Noskip);

        // Phase 44: 设置 instant 标记（say instant=true 时跳过打字机效果）
        ctx.State.Set(StateKeys.Dialog.Instant, sd.Instant);

        // Phase 65: 模板三级优先级——say template > character screen > null(全局默认)
        var template = sd.Template ?? GetCharProp(charDef, "screen");
        ctx.State.Set(StateKeys.Dialog.Template, template);

        // 行内语音（DSL: say "text" voice="..."）——单轨原子替换，随前进自动停止
        // 回溯重放时同样播放（符合直觉：重看一句应听到同一句语音）
        if (!string.IsNullOrEmpty(sd.VoicePath))
        {
            ctx.State.Set(StateKeys.Audio.VoiceAutoStop, true);
            ctx.AudioManager?.PlayVoice(sd.VoicePath);
        }

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
    /// <para>回溯重展示（RollbackAsync/RollforwardAsync）时不记录历史，避免重复。</para>
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
        var appendText = ed.Append;
        // 原文翻译（I18n）
        if (ctx.I18n != null && !string.IsNullOrEmpty(appendText))
            appendText = ctx.I18n.Translate(appendText);
        var cur = ctx.State.Get<string>(StateKeys.Dialog.Text) ?? "";
        ctx.State.Set(StateKeys.Dialog.Text, cur + appendText);
        ctx.State.Set(StateKeys.Dialog.Complete, false);
    }
}
