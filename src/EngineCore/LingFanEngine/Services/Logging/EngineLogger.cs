using System.Runtime.CompilerServices;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Logging;

/// <summary>
/// 引擎日志默认实现——高性能、AOT 友好。
/// <para>设计要点：</para>
/// <para>1. 级别门控在最前（level &lt; MinimumLevel 直接 return），低于门控零分配。</para>
/// <para>2. Sink 数组预组装，无运行时委托分配。</para>
/// <para>3. Sink 异常隔离——日志系统自身绝不抛出影响引擎主流程。</para>
/// <para>4. 上下文属性仅在非空时构建，避免热路径不必要分配。</para>
/// <para>5. Caller 信息由编译器填充，AOT 安全无反射。</para>
/// </summary>
internal sealed class EngineLogger : IEngineLogger
{
    private readonly IEngineLogContextAccessor _contextAccessor;
    private readonly IEngineLogSink[] _sinks;
    private EngineLogLevel _minimumLevel;

    /// <inheritdoc/>
    public string Category { get; }

    /// <inheritdoc/>
    public EngineLogLevel MinimumLevel
    {
        get => _minimumLevel;
        set => _minimumLevel = value;
    }

    /// <summary>
    /// 创建引擎日志实例。
    /// </summary>
    /// <param name="category">日志分类名</param>
    /// <param name="contextAccessor">日志上下文访问器</param>
    /// <param name="sinks">预组装的 Sink 数组</param>
    public EngineLogger(
        string category,
        IEngineLogContextAccessor contextAccessor,
        IEngineLogSink[] sinks)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
        _minimumLevel = EngineLogLevel.Info;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsEnabled(EngineLogLevel level) => level >= _minimumLevel;

    /// <inheritdoc/>
    public void Log(
        EngineLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        // 热路径门控——低于最低级别直接返回，零分配
        if (level < _minimumLevel)
            return;

        // 构建上下文属性（仅在非空时分配字典）
        var context = _contextAccessor.Current;
        IReadOnlyDictionary<string, object?>? properties = null;
        if (!context.IsEmpty)
        {
            properties = context.BuildProperties();
        }

        // 创建日志条目（单次分配）
        var entry = new EngineLogEntry
        {
            Level = level,
            Category = Category,
            Message = message,
            ExceptionText = exception?.ToString(),
            Member = string.IsNullOrEmpty(member) ? null : member,
            FilePath = string.IsNullOrEmpty(file) ? null : file,
            LineNumber = line,
            Properties = properties
        };

        // 分发到所有 Sink，单个 Sink 异常不中断其他 Sink
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Write(entry);
            }
            catch (Exception ex)
            {
                // 日志系统自身异常吞掉，绝不影响引擎主流程
                System.Diagnostics.Debug.WriteLine($"[EngineLogger] Sink Write 失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 刷新所有 Sink 缓冲区。
    /// </summary>
    public void Flush()
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Flush();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EngineLogger] Sink Flush 失败: {ex.Message}");
            }
        }
    }
}
