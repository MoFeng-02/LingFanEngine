using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.AI;

/// <summary>Agent 工具：名称/描述/JSON Schema + 执行器。副作用由编排层（审批）决定。</summary>
public sealed class AgentTool
{
    /// <summary>工具名（模型据此调用）</summary>
    public required string Name { get; init; }

    /// <summary>工具描述（喂给模型）</summary>
    public required string Description { get; init; }

    /// <summary>参数 JSON Schema 文本</summary>
    public required string ParametersJson { get; init; }

    /// <summary>执行器：入参为解析后的 JSON 元素，返回结果文本（喂回模型）。</summary>
    public required Func<JsonElement, CancellationToken, Task<string>> Execute { get; init; }

    public ModelTool ToModelTool() => new(Name, Description, ParametersJson);
}

/// <summary>Agent 单轮运行结果</summary>
public sealed record AgentRunResult(
    string? FinalText,
    IReadOnlyList<string> ToolCalls,
    UsageStats Usage,
    bool Succeeded);

/// <summary>
/// 通用 agent 循环（ReAct）：模型决定调工具 → 执行 → 结果回喂 → 直到无工具调用或达上限。
/// <para>所有工具经 <see cref="IModelClient"/> 的 function calling 完成；<paramref name="usage"/> 由调用方传入并累计。</para>
/// </summary>
public static class AgentExecutor
{
    /// <summary>
    /// 运行 agent 循环。
    /// </summary>
    /// <param name="client">模型客户端（需支持工具）</param>
    /// <param name="tools">可用工具集（同一次运行内全部暴露；动态子集由调用方决定）</param>
    /// <param name="messages">初始消息（含 system）</param>
    /// <param name="usage">用法用量累计（调用方传入的空 UsageStats 会被回填）</param>
    /// <param name="maxIterations">最大循环次数（防死循环护栏）</param>
    public static async Task<AgentRunResult> RunAsync(
        IModelClient client, IReadOnlyList<AgentTool> tools, List<ModelMessage> messages,
        UsageStats usage, int maxIterations, CancellationToken ct, Action<string>? onStep = null)
    {
        var modelTools = tools.Select(t => t.ToModelTool()).ToList();
        var executed = new List<string>();
        string? final = null;
        var succeeded = false;

        for (var i = 0; i < maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            onStep?.Invoke($"Agent 第 {i + 1}/{maxIterations} 轮：正在与模型通信…");
            var resp = await client.CompleteAsync(messages, new ModelRequestOptions
            {
                Tools = modelTools,
                Temperature = 0.2m,
                MaxTokens = 1024,
                UsageCallback = u => usage.Merge(u),
            }, ct).ConfigureAwait(false);

            if (resp == null)
                return new AgentRunResult(final, executed, usage, false);

            final = resp.Content;
            if (resp.ToolCalls is not { Count: > 0 })
            {
                succeeded = true; // 模型无更多工具调用 → 收敛
                break;
            }

            messages.Add(new ModelMessage("assistant", resp.Content ?? "", resp.ToolCalls));
            foreach (var tc in resp.ToolCalls)
            {
                onStep?.Invoke($"正在执行工具「{tc.Name}」…");
                var tool = tools.FirstOrDefault(t => t.Name == tc.Name);
                string resultText;
                if (tool == null)
                {
                    resultText = "{ \"error\": \"unknown tool: " + Escape(tc.Name) + "\" }";
                }
                else
                {
                    try
                    {
                        JsonElement args = default;
                        if (!string.IsNullOrWhiteSpace(tc.ArgumentsJson))
                        {
                            using var doc = JsonDocument.Parse(tc.ArgumentsJson);
                            args = doc.RootElement.Clone();
                        }
                        resultText = await tool.Execute(args, ct).ConfigureAwait(false);
                        executed.Add(tc.Name);
                    }
                    catch (Exception ex)
                    {
                        resultText = "{ \"error\": \"" + Escape(ex.Message) + "\" }";
                    }
                }
                messages.Add(new ModelMessage("tool", resultText, null, tc.Id));
            }
        }

        return new AgentRunResult(final, executed, usage, succeeded);
    }

    private static string Escape(string s)
        => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}