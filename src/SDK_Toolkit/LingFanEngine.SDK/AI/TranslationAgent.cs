using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.AI;

/// <summary>
/// 翻译 Agent 编排——用 tool calling 理解项目、决定翻译，文件写入收敛到 <see cref="ITranslationService"/> 确定性路径。
/// <para>工具：<c>translation.status</c>（只读摘要）与 <c>translation.sync</c>（调用 PrepareSyncAsync 出编辑，写入待审批）。</para>
/// </summary>
public sealed class TranslationAgent : ITranslationAgent
{
    private readonly IModelClient _client;
    private readonly ITranslationService _translation;
    private readonly ITranslator _translator;

    /// <summary>创建翻译 Agent（client + translator 由调用方构建；translator 通常为 LlmTranslator 包装同一 client）。</summary>
    public TranslationAgent(IModelClient client, ITranslationService translation, ITranslator translator)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _translation = translation ?? throw new ArgumentNullException(nameof(translation));
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
    }

    /// <inheritdoc/>
    public async Task<TranslationAgentResult> RunAsync(TranslationAgentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_client.SupportsTools)
            return new TranslationAgentResult { FinalText = "当前模型不支持工具调用，请走快速批译路径。", ToolCalls = [], Usage = new UsageStats() };

        var state = new SyncState();
        var tools = BuildTools(request, state);
        var usage = new UsageStats();
        var messages = new List<ModelMessage>
        {
            new("system", BuildSystemPrompt(request)),
            new("user", "请完成翻译：先 inspect 状态，再翻译生成文件。完成后简要说明结果。"),
        };

        var run = await AgentExecutor.RunAsync(_client, tools, messages, usage, request.MaxIterations, ct, request.OnStep).ConfigureAwait(false);

        var sync = state.Prepared;
        bool approved = false;
        if (sync != null)
        {
            var edits = sync.PendingEdits ?? [];
            if (edits.Count == 0)
                approved = true; // 无变更无需审批
            else if (request.AutoApprove)
                approved = true;
            else if (request.ApproveAsync != null)
                approved = await request.ApproveAsync(edits, sync.PreviewText, ct).ConfigureAwait(false);
            // 默认（无回调且非 Auto）：不批准，避免静默写盘
        }

        int applied = 0;
        if (approved && sync?.PendingEdits is { Count: > 0 })
            applied = await _translation.ApplyEditsAsync(sync.PendingEdits, ct).ConfigureAwait(false);

        return new TranslationAgentResult
        {
            FinalText = run.FinalText,
            ToolCalls = run.ToolCalls,
            Usage = run.Usage,
            Sync = sync,
            Approved = approved && sync != null,
            AppliedEdits = applied,
        };
    }

    private List<AgentTool> BuildTools(TranslationAgentRequest request, SyncState state)
    {
        return
        [
            new AgentTool
            {
                Name = "translation.status",
                Description = "返回当前项目的翻译状态摘要：语言、布局、扫描到的文本数、待翻译文本数。无参数。",
                ParametersJson = "{}",
                Execute = (_, _) => StatusAsync(request.ProjectDir, request.TargetLang),
            },
            new AgentTool
            {
                Name = "translation.sync",
                Description = "按布局拉取/翻译并生成翻译文件（不直接写盘，产出待审批变更）。调用比生成翻译文件。返回文件数/新增/保留/预览开头。",
                ParametersJson = "{}",
                Execute = (_, toolCt) => SyncAsync(request, state, toolCt),
            },
        ];
    }

    private async Task<string> StatusAsync(string projectDir, string lang)
    {
        var scanned = await _translation.ScanTranslatableTextsAsync(projectDir).ConfigureAwait(false);
        var status = await _translation.GetTranslationStatusAsync(projectDir, lang).ConfigureAwait(false);
        var distinct = scanned.Select(s => s.Text).Distinct(StringComparer.Ordinal).ToList();
        var pending = distinct.Count(t => !status.TryGetValue(t, out var v) || !v);
        return "{\"scanned\":" + distinct.Count + ",\"pending\":" + pending + ",\"lang\":\"" + lang + "\"}";
    }

    private async Task<string> SyncAsync(TranslationAgentRequest request, SyncState state, CancellationToken ct)
    {
        var result = await _translation.PrepareSyncAsync(
            request.ProjectDir, request.TargetLang, request.Layout, _translator,
            request.Progress, false, request.SourceLang, ct).ConfigureAwait(false);
        state.Prepared = result;

        return "{\"files\":" + (result.PendingEdits?.Count ?? 0)
            + ",\"added\":" + result.Added
            + ",\"kept\":" + result.Kept
            + ",\"translated\":" + result.Translated
            + ",\"failed\":" + result.Failed
            + ",\"pendingApproval\":true}";
    }

    private static string BuildSystemPrompt(TranslationAgentRequest request)
    {
        var layout = request.Layout switch
        {
            TranslationLayout.Mirrored => "Mirrored（镜像 Stories 子文件夹，逐 story 一个 json）",
            TranslationLayout.SingleFile => "SingleFile（Resources/Lang/{lang}.json 单文件）",
            _ => "Flat（Resources/Lang/{lang}/main.json + 按场景扁平 json）",
        };
        return
            "You are an assistant for LingFan game localization. " +
            $"Project root: {request.ProjectDir}; target language: {request.TargetLang} (locale code, used as Resources/Lang/{request.TargetLang}/ directory); layout: {layout}. " +
            "Available tools let you inspect translation status and generate the translation files. " +
            "Call translation.status to inspect, then translation.sync to prepare the file(s) by the chosen layout (this produces a changeset for approval, not direct write). " +
            "After that, reply briefly with the outcome. Placeholders like {var}, {b}, {color=#FFF} are preserved programmatically; do not worry about them.";
    }

    private sealed class SyncState
    {
        public TranslationSyncResult? Prepared { get; set; }
    }
}