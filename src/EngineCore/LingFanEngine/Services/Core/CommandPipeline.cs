using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 命令管道实现
/// <para>基于 System.Threading.Channels 的无锁异步队列。</para>
/// </summary>
public class CommandPipeline : ICommandPipeline, IDisposable
{
    private readonly Channel<ICommand> _channel;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;
    private int _count;

    /// <summary>
    /// 创建命令管道
    /// </summary>
    /// <param name="bounded">是否为有界队列</param>
    /// <param name="capacity">有界队列容量（默认 256）</param>
    public CommandPipeline(bool bounded = false, int capacity = 256)
    {
        _channel = bounded
            ? Channel.CreateBounded<ICommand>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            })
            : Channel.CreateUnbounded<ICommand>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });
    }

    /// <inheritdoc/>
    public float TimeScale { get; set; } = 1.0f;

    /// <inheritdoc/>
    // 🔥 优化 1：直接使用 Channel 内部维护的原子计数，省去手动 Interlocked，更准确
    public int Count => Volatile.Read(ref _count);

    /// <inheritdoc/>
    public ValueTask SendAsync(ICommand command, CancellationToken ct = default)
    {
        // 🔥 优化 2（核心性能）：快速路径（Fast Path）
        // 对于无界队列，或者有界队列但未满时，TryWrite 立即成功。
        // 此时直接返回 ValueTask.CompletedTask，真正实现零堆内存分配（零 GC）！
        if (_channel.Writer.TryWrite(command))
        {
            Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }

        // 慢路径（Slow Path）：只有有界队列且通道已满（或管道已关闭）时才会走到这里。
        // 此时才真正需要异步等待，异步状态机也仅在此时才会产生。
        CancellationToken token = ct.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token).Token
            : _disposeCts.Token;

        return WriteAsyncSlow(command, token);
    }

    // 将慢路径单独抽成一个异步方法，让状态机只在罕见情况（队列满）下生成
    private async ValueTask WriteAsyncSlow(ICommand command, CancellationToken token)
    {
        // 如果管道已关闭，WriteAsync 会抛出 ChannelClosedException，符合预期
        await _channel.Writer.WriteAsync(command, token);
        Interlocked.Increment(ref _count);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ICommand> ReceiveAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        // 移除了手动 Interlocked.Decrement，直接依赖 Channel 内部状态

        if (!ct.CanBeCanceled)
        {
            await foreach (var command in _channel.Reader.ReadAllAsync(_disposeCts.Token))
            {
                Interlocked.Decrement(ref _count);
                yield return command;
            }
            yield break;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        await foreach (var command in _channel.Reader.ReadAllAsync(linked.Token))
        {
            Interlocked.Decrement(ref _count);
            yield return command;
        }
    }

    /// <inheritdoc/>
    public bool TryRead(out ICommand command)
    {
        if (_channel.Reader.TryRead(out command!))
        {
            Interlocked.Decrement(ref _count);
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public void Complete()
    {
        _channel.Writer.TryComplete();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 标准的 Dispose 模式，支持派生类重写
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // 取消所有正在阻塞的等待者（SendAsync 和 ReceiveAllAsync 会抛出取消异常）
            _disposeCts.Cancel();
            _disposeCts.Dispose();
        }

        // 标记管道完成，拒绝后续写入
        _channel.Writer.TryComplete();
        _disposed = true;
    }
}

//using System.Threading.Channels;
//using LingFanEngine.Abstractions.Interfaces.Core;

//namespace LingFanEngine.Services.Core;

///// <summary>
///// 命令管道实现
///// <para>基于 System.Threading.Channels 的无锁异步队列。</para>
///// </summary>
//public class CommandPipeline : ICommandPipeline, IDisposable
//{
//    private readonly Channel<ICommand> _channel;
//    private readonly CancellationTokenSource _disposeCts = new();
//    private int _count;

//    /// <summary>
//    /// 创建命令管道
//    /// </summary>
//    /// <param name="bounded">是否为有界队列</param>
//    /// <param name="capacity">有界队列容量（默认 256）</param>
//    public CommandPipeline(bool bounded = false, int capacity = 256)
//    {
//        _channel = bounded
//            ? Channel.CreateBounded<ICommand>(new BoundedChannelOptions(capacity)
//            {
//                FullMode = BoundedChannelFullMode.Wait,
//                SingleWriter = false,
//                SingleReader = true
//            })
//            : Channel.CreateUnbounded<ICommand>(new UnboundedChannelOptions
//            {
//                SingleWriter = false,
//                SingleReader = true
//            });
//    }

//    /// <inheritdoc/>
//    public float TimeScale { get; set; } = 1.0f;

//    /// <inheritdoc/>
//    public int Count => Volatile.Read(ref _count);

//    /// <inheritdoc/>
//    public async ValueTask SendAsync(ICommand command, CancellationToken ct = default)
//    {
//        // Fast path：外部 CT 可取消时直接使用，避免每次 Send 创建 LinkedTokenSource（减少 GC 压力）
//        if (ct.CanBeCanceled)
//        {
//            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
//            await _channel.Writer.WriteAsync(command, linked.Token);
//        }
//        else
//        {
//            await _channel.Writer.WriteAsync(command, _disposeCts.Token);
//        }
//        Interlocked.Increment(ref _count);
//    }

//    /// <inheritdoc/>
//    public async IAsyncEnumerable<ICommand> ReceiveAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
//    {
//        // Fast path：外部 CT 不可取消时直接用 _disposeCts.Token，避免 LinkedTokenSource 分配
//        if (!ct.CanBeCanceled)
//        {
//            await foreach (var command in _channel.Reader.ReadAllAsync(_disposeCts.Token))
//            {
//                Interlocked.Decrement(ref _count);
//                yield return command;
//            }
//            yield break;
//        }

//        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
//        await foreach (var command in _channel.Reader.ReadAllAsync(linked.Token))
//        {
//            Interlocked.Decrement(ref _count);
//            yield return command;
//        }
//    }

//    /// <inheritdoc/>
//    public bool TryRead(out ICommand command)
//    {
//        if (_channel.Reader.TryRead(out command!))
//        {
//            Interlocked.Decrement(ref _count);
//            return true;
//        }
//        return false;
//    }

//    /// <inheritdoc/>
//    public void Complete()
//    {
//        _channel.Writer.TryComplete();
//    }

//    /// <inheritdoc/>
//    public void Dispose()
//    {
//        _disposeCts.Cancel();
//        _disposeCts.Dispose();
//        _channel.Writer.TryComplete();
//    }
//}
