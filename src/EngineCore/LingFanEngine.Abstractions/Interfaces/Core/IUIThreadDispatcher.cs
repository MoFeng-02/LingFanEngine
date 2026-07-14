namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// UI 线程调度器抽象——将 UI 框架的线程调度逻辑从引擎核心中解耦。
/// <para>引擎核心（GameLoop 等）通过此接口在 UI 线程上执行操作，不直接依赖 Avalonia。</para>
/// <para>实现方在组合根注册（如 AvaloniaUIThreadDispatcher 封装 Dispatcher.UIThread.Post）。</para>
/// </summary>
public interface IUIThreadDispatcher
{
    /// <summary>
    /// 将操作投递到 UI 线程异步执行。
    /// </summary>
    /// <param name="action">要在 UI 线程上执行的操作。</param>
    /// <param name="highPriority">是否使用高优先级（如首帧）；false=渲染优先级（默认，避免抢占用户输入）。</param>
    void Post(Action action, bool highPriority = false);
}
