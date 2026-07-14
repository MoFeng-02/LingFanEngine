using Avalonia.Threading;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

/// <summary>
/// Avalonia UI 线程调度器实现——封装 Dispatcher.UIThread.Post。
/// <para>highPriority=true 映射到 DispatcherPriority.Normal（首帧等高优先级场景），</para>
/// <para>highPriority=false 映射到 DispatcherPriority.Render（默认，后续帧使用渲染优先级避免抢占用户输入）。</para>
/// </summary>
public class AvaloniaUIThreadDispatcher : IUIThreadDispatcher
{
    public void Post(Action action, bool highPriority = false)
    {
        var priority = highPriority ? DispatcherPriority.Normal : DispatcherPriority.Render;
        Dispatcher.UIThread.Post(action, priority);
    }
}
