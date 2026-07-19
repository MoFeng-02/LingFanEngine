using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Services.Logging.Sinks;

namespace LingFanEngine.Services.Logging;

/// <summary>
/// 引擎日志工厂实现——按配置组装 Sink 链，创建分类 Logger。
/// <para>工厂自身是 Singleton，创建的 Logger 会被各服务缓存为 readonly 字段。</para>
/// <para>Sink 组装在工厂构造时完成一次，后续 Create 调用零分配（共享 Sink 数组）。</para>
/// <para>实现 IDisposable——应用关闭时 Dispose 所有 Sink（如 FileSink 的 StreamWriter）。</para>
/// <para>AOT 友好：无反射，无动态类型加载。</para>
/// </summary>
internal sealed class EngineLoggerFactory : IEngineLoggerFactory, IDisposable
{
    private readonly IEngineLogContextAccessor _contextAccessor;
    private readonly IEngineLogSink[] _sinks;
    private readonly EngineLogLevel _minimumLevel;

    /// <summary>
    /// 创建日志工厂。
    /// </summary>
    /// <param name="options">引擎配置选项</param>
    /// <param name="contextAccessor">日志上下文访问器</param>
    /// <param name="serviceProvider">DI 服务提供者（用于按需获取 IDebugConsoleService）</param>
    public EngineLoggerFactory(
        LingFanEngineOptions options,
        IEngineLogContextAccessor contextAccessor,
        IServiceProvider serviceProvider)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _minimumLevel = options.Logging.MinimumLevel;
        _sinks = BuildSinks(options, serviceProvider);
    }

    /// <inheritdoc/>
    public IEngineLogger Create(string category)
    {
        return new EngineLogger(category, _contextAccessor, _sinks)
        {
            MinimumLevel = _minimumLevel
        };
    }

    /// <summary>
    /// 按配置构建 Sink 数组。
    /// <para>WASM 平台自动剔除 File Sink（文件系统不可用）。</para>
    /// </summary>
    private static IEngineLogSink[] BuildSinks(
        LingFanEngineOptions options,
        IServiceProvider serviceProvider)
    {
        var logging = options.Logging;
        var sinks = new List<IEngineLogSink>(4);

        // DebugTrace Sink
        if (logging.Sinks.HasFlag(LoggingSinks.DebugTrace))
        {
            sinks.Add(new DebugTraceSink());
        }

        // Console Sink
        if (logging.Sinks.HasFlag(LoggingSinks.Console))
        {
            sinks.Add(new ConsoleSink());
        }

        // File Sink（WASM 不支持文件系统）
        if (logging.Sinks.HasFlag(LoggingSinks.File) && !OperatingSystem.IsBrowser())
        {
            var logDir = Path.Combine(options.SaveDirectory, "Logs");
            sinks.Add(new FileSink(logDir, logging.FileRetentionDays));
        }

        // DebugConsole 镜像 Sink（可选）
        if (logging.MirrorToDebugConsole)
        {
            var debugConsole = serviceProvider.GetService(typeof(IDebugConsoleService))
                as IDebugConsoleService;
            if (debugConsole is not null)
            {
                sinks.Add(new DebugConsoleMirrorSink(debugConsole, logging.MirrorMinimumLevel));
            }
        }

        return sinks.Count > 0
            ? sinks.ToArray()
            : [new DebugTraceSink()]; // 兜底：至少有一个 Sink
    }

    /// <summary>
    /// 释放所有 Sink 资源（如 FileSink 的 StreamWriter）。
    /// <para>由 DI 容器在应用关闭时自动调用。</para>
    /// </summary>
    public void Dispose()
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EngineLoggerFactory] Sink Dispose 失败: {ex.Message}");
            }
        }
    }
}
