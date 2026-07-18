using System.Text;
using LingFanEngine.Abstractions.Interfaces.Logging;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Logging.Sinks;

/// <summary>
/// 文件输出 Sink——按天滚动写入日志文件。
/// <para>文件命名：engine-{yyyy-MM-dd}.log，跨天自动切换文件。</para>
/// <para>启动时自动清理超过保留天数的旧日志文件。</para>
/// <para>线程安全：lock 保护文件写入。</para>
/// <para>AOT 友好：无反射，纯文件 IO。</para>
/// <para>WASM 平台不注册此 Sink（文件系统不可用）。</para>
/// </summary>
internal sealed class FileSink : IEngineLogSink
{
    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string? _currentDate;

    /// <summary>
    /// 创建文件 Sink。
    /// </summary>
    /// <param name="directory">日志目录路径（自动创建）</param>
    /// <param name="retentionDays">日志保留天数（超过的旧文件自动清理）</param>
    public FileSink(string directory, int retentionDays)
    {
        _directory = directory;
        _retentionDays = retentionDays > 0 ? retentionDays : 7;

        try
        {
            Directory.CreateDirectory(_directory);
            CleanOldFiles();
        }
        catch
        {
            // 目录创建失败不抛异常——文件 Sink 降级为静默
            // 引擎主流程不受影响
        }
    }

    public void Write(EngineLogEntry entry)
    {
        lock (_lock)
        {
            if (!EnsureWriter(entry.Timestamp))
                return; // 文件不可用，静默跳过

            try
            {
                _writer!.WriteLine(FormatFileEntry(entry));
                _writer.Flush();
            }
            catch
            {
                // 写入失败不抛异常——降级为静默
                _writer?.Dispose();
                _writer = null;
                _currentDate = null;
            }
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            _writer?.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
            _currentDate = null;
        }
    }

    /// <summary>
    /// 确保写入器已就绪且对应正确的日期。
    /// <para>跨天时自动切换文件。返回 false 表示文件不可用。</para>
    /// </summary>
    private bool EnsureWriter(DateTimeOffset timestamp)
    {
        var dateStr = timestamp.ToString("yyyy-MM-dd");

        if (_currentDate == dateStr && _writer is not null)
            return true;

        // 日期变更或首次写入——切换文件
        _writer?.Dispose();

        try
        {
            var path = Path.Combine(_directory, $"engine-{dateStr}.log");
            var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };
            _currentDate = dateStr;
            return true;
        }
        catch
        {
            _writer = null;
            _currentDate = null;
            return false;
        }
    }

    /// <summary>
    /// 清理超过保留天数的旧日志文件。
    /// </summary>
    private void CleanOldFiles()
    {
        try
        {
            var cutoff = DateTimeOffset.Now.AddDays(-_retentionDays);
            foreach (var file in Directory.GetFiles(_directory, "engine-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff.DateTime)
                        File.Delete(file);
                }
                catch
                {
                    // 单个文件清理失败不影响其他文件
                }
            }
        }
        catch
        {
            // 清理失败不影响引擎启动
        }
    }

    /// <summary>
    /// 格式化文件日志条目（带时间戳前缀）。
    /// <para>格式：yyyy-MM-dd HH:mm:ss.fff [级别] [分类] 消息 — 异常 | 调用方 {属性}</para>
    /// </summary>
    private static string FormatFileEntry(EngineLogEntry entry)
    {
        var sb = new StringBuilder(192);
        sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        sb.Append(' ').Append(DebugTraceSink.FormatEntry(entry));
        return sb.ToString();
    }
}
