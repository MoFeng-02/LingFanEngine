namespace LingFanEngine.Abstractions.Interfaces.Logging;

/// <summary>
/// 引擎日志上下文访问器——基于 AsyncLocal 异步流转。
/// <para>用于在日志中自动附加当前场景/时间等上下文，无需每处手传。</para>
/// <para>通过 <see cref="IEngineLogContext.BeginScope"/> 推入上下文栈，using 块结束自动恢复。</para>
/// </summary>
public interface IEngineLogContextAccessor
{
    /// <summary>当前日志上下文（永不为 null，空上下文返回 Empty 实例）</summary>
    IEngineLogContext Current { get; }
}
