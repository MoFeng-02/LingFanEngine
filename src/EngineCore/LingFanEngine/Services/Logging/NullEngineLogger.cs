using System.Runtime.CompilerServices;
using LingFanEngine.Abstractions.Interfaces.Logging;

namespace LingFanEngine.Services.Logging;

/// <summary>
/// 空日志实现——所有方法均为 no-op。
/// <para>用于未注入 IEngineLoggerFactory 的场景（如单元测试）。</para>
/// <para>IsEnabled 始终返回 false，Log 方法直接返回，零开销。</para>
/// </summary>
internal sealed class NullEngineLogger : IEngineLogger
{
    /// <summary>单例实例</summary>
    public static readonly NullEngineLogger Instance = new();

    /// <inheritdoc/>
    public string Category => "Null";

    /// <inheritdoc/>
    public EngineLogLevel MinimumLevel { get; set; } = EngineLogLevel.Critical;

    /// <inheritdoc/>
    public bool IsEnabled(EngineLogLevel level) => false;

    /// <inheritdoc/>
    public void Log(
        EngineLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        // no-op
    }
}
