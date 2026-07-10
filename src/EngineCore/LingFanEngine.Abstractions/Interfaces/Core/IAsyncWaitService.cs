namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 异步等待服务——基于状态变更事件的零延迟等待
/// <para>订阅 <see cref="IStateContainer.ValueChanged"/> 事件，当状态变更时检查谓词，
/// 满足则通过 TaskCompletionSource 零延迟唤醒等待方。</para>
/// <para>替代 Task.Delay 轮询模式，消除 0-16ms 的轮询间隔延迟。</para>
/// </summary>
public interface IAsyncWaitService
{
    /// <summary>
    /// 等待条件满足——监听状态变更，每次变更时检查谓词
    /// </summary>
    /// <param name="predicate">条件谓词（返回 true 时完成等待）</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="ct">取消令牌（回溯/停止时取消）</param>
    /// <returns>谓词满足时正常完成；超时或取消时抛出异常</returns>
    Task WaitForAsync(Func<bool> predicate, TimeSpan timeout, CancellationToken ct = default);
}
