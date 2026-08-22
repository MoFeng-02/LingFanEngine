using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>翻译 Agent 请求参数。</summary>
public sealed class TranslationAgentRequest
{
    /// <summary>项目根目录</summary>
    public required string ProjectDir { get; init; }

    /// <summary>目标语言（locale code，作 Lang/{lang}/ 目录名）</summary>
    public required string TargetLang { get; init; }

    /// <summary>翻译输出布局</summary>
    public required TranslationLayout Layout { get; init; }

    /// <summary>源语言（留空=自动检测）</summary>
    public string SourceLang { get; init; } = "";

    /// <summary>自动批准文件写入（默认 false，需人在环审批）</summary>
    public bool AutoApprove { get; init; }

    /// <summary>审批回调（AutoApprove=false 时，给定整轮变更集与预览，返回是否批准）</summary>
    public Func<IReadOnlyList<FileEdit>, string, CancellationToken, Task<bool>>? ApproveAsync { get; init; }

    /// <summary>最大循环次数（防死循环护栏）</summary>
    public int MaxIterations { get; init; } = 12;

    /// <summary>翻译进度回调（翻译阶段）</summary>
    public IProgress<TranslationProgress>? Progress { get; init; }

    /// <summary>过程状态回调：Agent「理解项目 + tool calling」阶段的分步实时反馈（每轮/每工具触发）</summary>
    public Action<string>? OnStep { get; init; }
}

/// <summary>翻译 Agent 一次运行结果。</summary>
public sealed class TranslationAgentResult
{
    /// <summary>模型最终回复文本</summary>
    public string? FinalText { get; init; }

    /// <summary>实际执行的工具名列表</summary>
    public IReadOnlyList<string> ToolCalls { get; init; } = [];

    /// <summary>本次运行累计用量</summary>
    public UsageStats Usage { get; init; } = new();

    /// <summary>已准备的翻译同步结果（含待审批编辑与预览；可能为 null）</summary>
    public TranslationSyncResult? Sync { get; init; }

    /// <summary>变更是否获批</summary>
    public bool Approved { get; init; }

    /// <summary>实际提交的编辑数</summary>
    public int AppliedEdits { get; init; }
}

/// <summary>
/// 翻译 Agent——让 LLM 经 tool calling 理解项目、决定翻译，并把文件写入收敛到 <see cref="ITranslationService"/>
/// 的确定性路径（<see cref="ITranslationService.PrepareSyncAsync"/> 出编辑 + 审批 + <see cref="ITranslationService.ApplyEditsAsync"/> 提交）。
/// <para>保留「快速批译」确定性回退（不启用 Agent 时直接走 <see cref="ITranslationService"/>）。</para>
/// </summary>
public interface ITranslationAgent
{
    /// <summary>运行翻译 Agent。</summary>
    Task<TranslationAgentResult> RunAsync(TranslationAgentRequest request, CancellationToken ct);
}