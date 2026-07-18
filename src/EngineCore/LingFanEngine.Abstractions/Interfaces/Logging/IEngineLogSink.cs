using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Abstractions.Interfaces.Logging;

/// <summary>
/// 引擎日志输出目标（Sink）——将日志条目写入具体目标。
/// <para>多个 Sink 可组合使用（如同时输出到 Debug + 文件 + 控制台）。</para>
/// <para>实现必须是线程安全的（可能被多线程并发调用）。</para>
/// <para>Sink 内部异常不应影响引擎主流程（EngineLogger 会吞掉 Sink 异常）。</para>
/// </summary>
public interface IEngineLogSink : IDisposable
{
    /// <summary>写入一条日志条目</summary>
    void Write(EngineLogEntry entry);

    /// <summary>刷新缓冲区（确保所有已写入的日志已落盘）</summary>
    void Flush();
}
