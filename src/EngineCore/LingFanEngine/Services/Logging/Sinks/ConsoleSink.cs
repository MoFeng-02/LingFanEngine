using System.Text;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Logging.Sinks;

/// <summary>
/// 控制台输出 Sink——写入标准输出流，带级别彩色。
/// <para>线程安全：lock 保护 Console.Out。</para>
/// <para>AOT 友好：无反射，纯控制台操作。</para>
/// <para>WASM/移动端控制台可能不可用，此时不注册此 Sink。</para>
/// </summary>
internal sealed class ConsoleSink : IEngineLogSink
{
    private readonly object _lock = new();

    public void Write(EngineLogEntry entry)
    {
        var line = DebugTraceSink.FormatEntry(entry);
        var color = LevelToConsoleColor(entry.Level);

        lock (_lock)
        {
            var previousColor = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(line);
            }
            finally
            {
                Console.ForegroundColor = previousColor;
            }
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            Console.Out.Flush();
        }
    }

    public void Dispose() { /* 无资源 */ }

    private static ConsoleColor LevelToConsoleColor(EngineLogLevel level) => level switch
    {
        EngineLogLevel.Trace => ConsoleColor.DarkGray,
        EngineLogLevel.Debug => ConsoleColor.Gray,
        EngineLogLevel.Info => ConsoleColor.White,
        EngineLogLevel.Warning => ConsoleColor.Yellow,
        EngineLogLevel.Error => ConsoleColor.Red,
        EngineLogLevel.Critical => ConsoleColor.Magenta,
        _ => ConsoleColor.White
    };
}
