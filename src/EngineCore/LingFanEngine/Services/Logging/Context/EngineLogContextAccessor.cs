using LingFanEngine.Abstractions.Interfaces.Logging;

namespace LingFanEngine.Services.Logging.Context;

/// <summary>
/// 日志上下文访问器实现——基于 AsyncLocal 异步流转。
/// <para>AsyncLocal 确保上下文在 async/await 调用链中正确流转。</para>
/// <para>BeginScope 推入上下文栈，Dispose 恢复前一个上下文（支持嵌套）。</para>
/// <para>AOT 友好：无反射，纯 AsyncLocal 操作。</para>
/// </summary>
internal sealed class EngineLogContextAccessor : IEngineLogContextAccessor
{
    private static readonly AsyncLocal<IEngineLogContext?> _current = new();

    /// <inheritdoc/>
    public IEngineLogContext Current => _current.Value ?? EngineLogContext.Empty;

    /// <summary>
    /// 推入新的上下文到 AsyncLocal 栈。
    /// <para>返回 IDisposable，Dispose 时恢复到前一个上下文。</para>
    /// <para>支持嵌套调用：内层 using 结束后恢复到外层上下文。</para>
    /// </summary>
    internal static IDisposable PushScope(IEngineLogContext context)
    {
        var previous = _current.Value;
        _current.Value = context;
        return new ScopeDisposer(previous);
    }

    /// <summary>
    /// 作用域释放器——Dispose 时恢复前一个上下文。
    /// <para>previous 为 null 时恢复到空上下文（Empty）。</para>
    /// </summary>
    private sealed class ScopeDisposer : IDisposable
    {
        private readonly IEngineLogContext? _previous;
        private bool _disposed;

        public ScopeDisposer(IEngineLogContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
