namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 命令管道接口
/// <para>基于 Channel 的无锁异步队列，支持优先级和时间缩放。</para>
/// </summary>
public interface ICommandPipeline
{
    /// <summary>
    /// 投递命令到管道
    /// </summary>
    ValueTask SendAsync(ICommand command, CancellationToken ct = default);

    /// <summary>
    /// 消费命令（主循环中调用，异步枚举）
    /// </summary>
    IAsyncEnumerable<ICommand> ReceiveAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 尝试读取一条命令（非阻塞）
    /// </summary>
    bool TryRead(out ICommand command);

    /// <summary>
    /// 管道中待处理的命令数量
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 全局时间缩放系数（1.0 = 正常，2.0 = 两倍速）
    /// </summary>
    float TimeScale { get; set; }

    /// <summary>
    /// 标记管道完成（主循环结束时调用）
    /// </summary>
    void Complete();
}
