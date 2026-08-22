using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LingFanEngine.SDK.Utils;

/// <summary>
/// 网络重试策略——对限流（429）与服务端错误（5xx）做指数退避 + 抖动重试。
/// <para>确定性可测：<see cref="ShouldRetry"/> 判断是否该重试；<see cref="DelayAsync"/> 计算退避间隔。
/// 默认生辰列表 429/500/502/503/504；非这些码不重试（4xx 业务错误直接返给调用方判级）。</para>
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>最大重试次数（不含首次请求）</summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>首次重试基准间隔</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>退避倍率（每次 × factor）</summary>
    public double BackoffFactor { get; init; } = 2.0;

    /// <summary>抖动比例（± factor，默认 ±20%）</summary>
    public double Jitter { get; init; } = 0.2;

    /// <summary>退避间隔上限</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>可重试的 HTTP 状态码集合</summary>
    public IReadOnlyList<int> RetryableStatusCodes { get; init; } = [429, 500, 502, 503, 504];

    /// <summary>判断 {statusCode} 在第 attempt 次失败后（attempt: 0 = 首次失败）是否应重试。</summary>
    public bool ShouldRetry(int statusCode, int attempt)
        => attempt < MaxAttempts && RetryableStatusCodes?.Contains(statusCode) == true;

    /// <summary>计算并等待第 attempt 次重试的退避间隔（attempt: 0 = 首次失败后的第一次重试）。</summary>
    public Task DelayAsync(int attempt, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var baseMs = Math.Min(InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt), MaxDelay.TotalMilliseconds);
        var jitter = Jitter * baseMs * (Random.Shared.NextDouble() * 2 - 1); // ±jitter
        var ms = Math.Max(0, baseMs + jitter);
        return Task.Delay(TimeSpan.FromMilliseconds(ms), ct);
    }

    /// <summary>计算第 attempt 次重试的退避间隔（不含抖动，供测试断言基础节奏）。</summary>
    public TimeSpan GetBaseDelay(int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt), MaxDelay.TotalMilliseconds));
}