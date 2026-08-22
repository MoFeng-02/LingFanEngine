using System;

namespace LingFanEngine.SDK.Models;

/// <summary>
/// AI 调用用量统计——累计输入/输出 token 与请求次数，供 UI 展示用量与成本。
/// <para>成本换算见 <see cref="UsageCostEstimate"/>（按显式单价，避免内置随时间漂移的价格表）。</para>
/// </summary>
public sealed class UsageStats
{
    /// <summary>累计输入 token</summary>
    public int InputTokens { get; set; }

    /// <summary>累计输出 token</summary>
    public int OutputTokens { get; set; }

    /// <summary>累计请求次数</summary>
    public int RequestCount { get; set; }

    /// <summary>累计输入+输出 token</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>累加一次用量（输入/输出 token + 请求计数）。</summary>
    public void Add(int inputTokens, int outputTokens)
    {
        InputTokens += inputTokens;
        OutputTokens += outputTokens;
        RequestCount++;
    }

    /// <summary>合并另一份用量到当前实例。</summary>
    public void Merge(UsageStats other)
    {
        if (other == null) return;
        InputTokens += other.InputTokens;
        OutputTokens += other.OutputTokens;
        RequestCount += other.RequestCount;
    }
}

/// <summary>
/// 成本换算工具——按"每 1M token 单价（USD，输入/输出分开）"估算请求成本。
/// <para>价格由调用方提供（配置文件或远程价格源），避免内置随时间漂移的硬编码表。</para>
/// </summary>
public static class UsageCostEstimate
{
    /// <summary>按每 1M token 单价（USD）估算一次/累计用量成本。</summary>
    /// <param name="inputTokens">输入 token 数</param>
    /// <param name="outputTokens">输出 token 数</param>
    /// <param name="inputPerMillionUsd">每 1M 输入 token 单价（USD）</param>
    /// <param name="outputPerMillionUsd">每 1M 输出 token 单价（USD）</param>
    /// <returns>估算成本（USD），四舍五入 6 位</returns>
    public static decimal Estimate(
        int inputTokens, int outputTokens, decimal inputPerMillionUsd, decimal outputPerMillionUsd)
    {
        var cost = inputTokens * inputPerMillionUsd / 1_000_000m
                 + outputTokens * outputPerMillionUsd / 1_000_000m;
        return Math.Round(cost, 6);
    }

    /// <summary>同 <see cref="Estimate(int,int,decimal,decimal)"/>，但以 <see cref="UsageStats"/> 为输入。</summary>
    public static decimal Estimate(UsageStats usage, decimal inputPerMillionUsd, decimal outputPerMillionUsd)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return Estimate(usage.InputTokens, usage.OutputTokens, inputPerMillionUsd, outputPerMillionUsd);
    }
}