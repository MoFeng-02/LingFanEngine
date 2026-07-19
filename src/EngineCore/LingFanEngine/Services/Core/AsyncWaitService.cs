using System.Collections.Immutable;
using System.Diagnostics;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 异步等待服务实现——基于 IStateContainer.ValueChanged 事件的零延迟等待
/// <para>订阅状态容器的值变更事件，当值变化时检查所有活跃等待的谓词，
/// 满足条件则通过 TaskCompletionSource 零延迟唤醒。</para>
/// <para>使用 RunContinuationsAsynchronously 避免 ValueChanged 回调线程被阻塞。</para>
/// <para>Phase 64：无锁设计——ImmutableArray + ImmutableInterlocked 原子替换。</para>
/// <para>选择 ImmutableArray 而非 ConcurrentDictionary 的理由：</para>
/// <para>1. 语义匹配——这是"集合"而非"键值映射"</para>
/// <para>2. 遍历性能——连续内存，CPU 缓存预取，比 Node 链表快 3-5 倍</para>
/// <para>3. GC 压力——无 Node 堆对象，短生命周期条目零 Gen0 垃圾</para>
/// <para>4. 写入成本——n 通常 1-10，O(n) 复制 &lt; 10ns，比哈希+分桶+Node 分配更快</para>
/// </summary>
public class AsyncWaitService : IAsyncWaitService
{
    private readonly IStateContainer _state;

    /// <summary>等待者集合——ImmutableArray 不可变快照，原子替换</summary>
    private ImmutableArray<WaitEntry> _waiters = ImmutableArray<WaitEntry>.Empty;

    /// <summary>
    /// 等待条目
    /// </summary>
    private class WaitEntry
    {
        public required Func<bool> Predicate;
        public required TaskCompletionSource<bool> Tcs;
        /// <summary>原子标记——Interlocked.Exchange 防止重复完成</summary>
        private int _isCompleted;
        public bool IsCompleted => Interlocked.CompareExchange(ref _isCompleted, 0, 0) != 0;
        public bool TryMarkCompleted() => Interlocked.Exchange(ref _isCompleted, 1) == 0;
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
            // 原子标记完成——TryMarkCompleted 保证只有一个路径会 TrySetCanceled
            if (entry.TryMarkCompleted())
            {
                if (ct.IsCancellationRequested)
                    tcs.TrySetCanceled(ct);
                else
                    tcs.TrySetCanceled(timeoutCts.Token);
            }
        });

        // 注册等待——ImmutableInterlocked.Update 原子添加（CAS + O(n) 复制，n 通常 1-10）
        ImmutableInterlocked.Update(ref _waiters, arr => arr.Add(entry));

        // Double-check：注册后再次检查谓词，防止注册到检查之间的竞态
        if (predicate())
        {
            if (entry.TryMarkCompleted())
                tcs.TrySetResult(true);
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
            // 移除等待者——ImmutableInterlocked.Update 原子移除
            ImmutableInterlocked.Update(ref _waiters, arr => arr.Remove(entry));
        }
    }

    /// <summary>
    /// 状态变更回调——检查所有活跃等待的谓词
    /// </summary>
    private void OnValueChanged(string key, object? value)
    {
        // 拍快照——原子读引用，ImmutableArray 不可变，遍历期间无竞态
        var snapshot = _waiters;

        // 快速检查：无等待者时直接返回
        if (snapshot.IsDefaultOrEmpty)
            return;

        // 连续内存遍历——CPU 缓存预取友好
        foreach (var w in snapshot)
        {
            // 已完成则跳过（原子检查）
            if (w.IsCompleted)
                continue;

            try
            {
                if (w.Predicate())
                {
                    // 原子标记完成——TryMarkCompleted 保证只有一个路径会 TrySetResult
                    if (w.TryMarkCompleted())
                        w.Tcs.TrySetResult(true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AsyncWaitService] 谓词异常: {ex.Message}");
            }
        }
    }
}
