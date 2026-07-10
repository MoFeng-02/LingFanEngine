using System.Diagnostics;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 异步等待服务实现——基于 IStateContainer.ValueChanged 事件的零延迟等待
/// <para>订阅状态容器的值变更事件，当值变化时检查所有活跃等待的谓词，
/// 满足条件则通过 TaskCompletionSource 零延迟唤醒。</para>
/// <para>使用 RunContinuationsAsynchronously 避免 ValueChanged 回调线程被阻塞。</para>
/// </summary>
public class AsyncWaitService : IAsyncWaitService
{
    private readonly IStateContainer _state;
    private readonly List<WaitEntry> _waiters = new();
    private readonly object _lock = new();

    /// <summary>
    /// 等待条目
    /// </summary>
    private class WaitEntry
    {
        public required Func<bool> Predicate;
        public required TaskCompletionSource<bool> Tcs;
        public bool IsCompleted;
    }

    public AsyncWaitService(IStateContainer state)
    {
        _state = state;
        _state.ValueChanged += OnValueChanged;
    }

    /// <inheritdoc/>
    public async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout, CancellationToken ct = default)
    {
        // Fast path：条件已满足
        if (predicate())
            return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new WaitEntry { Predicate = predicate, Tcs = tcs };

        // 超时 + 外部取消令牌
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        using var registration = timeoutCts.Token.Register(() =>
        {
            lock (_lock)
            {
                if (!entry.IsCompleted)
                {
                    entry.IsCompleted = true;
                    if (ct.IsCancellationRequested)
                        tcs.TrySetCanceled(ct);
                    else
                        tcs.TrySetCanceled(timeoutCts.Token);
                }
            }
        });

        // 注册等待
        lock (_lock)
        {
            _waiters.Add(entry);
        }

        // Double-check：注册后再次检查谓词，防止注册到检查之间的竞态
        if (predicate())
        {
            lock (_lock)
            {
                if (!entry.IsCompleted)
                {
                    entry.IsCompleted = true;
                    tcs.TrySetResult(true);
                }
            }
        }

        try
        {
            await tcs.Task;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 超时（非外部取消）——记录日志，正常返回
            Debug.WriteLine("[AsyncWaitService] WaitForAsync 超时，强制推进");
        }
        finally
        {
            lock (_lock)
            {
                _waiters.Remove(entry);
            }
        }
    }

    /// <summary>
    /// 状态变更回调——检查所有活跃等待的谓词
    /// </summary>
    private void OnValueChanged(string key, object? value)
    {
        // 快速检查：无等待者时直接返回（避免 lock 开销）
        List<WaitEntry>? toComplete = null;

        lock (_lock)
        {
            if (_waiters.Count == 0)
                return;

            foreach (var w in _waiters)
            {
                if (w.IsCompleted)
                    continue;

                try
                {
                    if (w.Predicate())
                    {
                        w.IsCompleted = true;
                        (toComplete ??= new List<WaitEntry>()).Add(w);
                    }
                }
                catch
                {
                    // 谓词异常不应影响其他等待者
                }
            }
        }

        // 在锁外完成 TCS，避免回调链持有锁
        if (toComplete != null)
        {
            foreach (var w in toComplete)
                w.Tcs.TrySetResult(true);
        }
    }
}
