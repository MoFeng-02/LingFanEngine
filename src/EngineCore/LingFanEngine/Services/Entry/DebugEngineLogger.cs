using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Entry;

/// <summary>
/// 默认日志实现——Debug 模式输出到 System.Diagnostics.Debug.WriteLine，Release 模式静默。
/// <para>游戏可通过 DI 注册自定义 IEngineLogger 替换。</para>
/// </summary>
public class DebugEngineLogger : IEngineLogger
{
    private readonly string _category;

    public DebugEngineLogger(string category = "Engine")
    {
        _category = category;
    }

    public void Log(EngineLogLevel level, string message, Exception? exception = null)
    {
#if DEBUG
        var prefix = level switch
        {
            EngineLogLevel.Debug => "[DBG]",
            EngineLogLevel.Info => "[INF]",
            EngineLogLevel.Warning => "[WRN]",
            EngineLogLevel.Error => "[ERR]",
            _ => "[???]"
        };
        var line = exception != null
            ? $"{prefix} [{_category}] {message} — {exception.GetType().Name}: {exception.Message}"
            : $"{prefix} [{_category}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
#endif
    }
}
