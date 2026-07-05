using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Resources;

/// <summary>
/// 热重载监视器
/// <para>使用 FileSystemWatcher 监控资源目录，检测到文件变更时自动重载并投递通知。</para>
/// </summary>
public class HotReloadWatcher : IDisposable
{
    private readonly ICommandPipeline _pipeline;
    private readonly IStateContainer _state;
    private FileSystemWatcher? _watcher;
    private readonly HashSet<string> _watchedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private bool _enabled = true;

    /// <summary>
    /// 文件变更事件（外部可订阅）
    /// </summary>
    public event Action<HotReloadEventArgs>? OnFileChanged;

    /// <summary>
    /// 是否启用热重载
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (_watcher != null)
                _watcher.EnableRaisingEvents = value;
        }
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    public HotReloadWatcher(ICommandPipeline pipeline, IStateContainer state)
    {
        _pipeline = pipeline;
        _state = state;
    }

    /// <summary>
    /// 开始监视指定目录
    /// </summary>
    /// <param name="directoryPath">要监视的目录路径</param>
    /// <param name="filter">文件过滤器（默认 "*.png;*.jpg;*.webp;*.wav;*.mp3;*.ogg;*.json"）</param>
    public void Watch(string directoryPath, string filter = "*.png;*.jpg;*.webp;*.wav;*.mp3;*.ogg;*.json")
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            return;
        }

        if (_watchedDirectories.Contains(directoryPath))
            return;

        _watchedDirectories.Add(directoryPath);

        // 如果还没有创建全局监视器，创建第一个
        if (_watcher == null)
        {
            _watcher = new FileSystemWatcher
            {
                Path = directoryPath,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = _enabled
            };

            // 将 filter 拆分为多个过滤器
            var filters = filter.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var f in filters)
            {
                _watcher.Filters.Add(f.Trim());
            }

            _watcher.Changed += OnChanged;
            _watcher.Created += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;

            // 防抖：300ms 内同一文件的多次变更只触发一次
            _watcher.InternalBufferSize = 65536;
        }
    }

    /// <summary>
    /// 停止监视
    /// </summary>
    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _watchedDirectories.Clear();
    }

    private DateTime _lastEvent;
    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // 防抖
        var now = DateTime.UtcNow;
        if ((now - _lastEvent).TotalMilliseconds < 300)
            return;
        _lastEvent = now;

        var args = new HotReloadEventArgs(e.FullPath, e.ChangeType);
        OnFileChanged?.Invoke(args);

        // 投递热重载命令到管道
        _pipeline.SendAsync(new SetVariableCommand
        {
            Key = $"{StateKeys.Animation.Prefix}hotreload_{e.FullPath}",
            Value = e.ChangeType.ToString()
        });

        // 标记需要刷新场景渲染缓存
        _state.Set(StateKeys.Scene.Dirty, true);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        var args = new HotReloadEventArgs(e.FullPath, WatcherChangeTypes.Renamed, e.OldFullPath);
        OnFileChanged?.Invoke(args);
        _state.Set(StateKeys.Scene.Dirty, true);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// 热重载事件参数
/// </summary>
public class HotReloadEventArgs : EventArgs
{
    /// <summary>文件路径</summary>
    public string FilePath { get; }

    /// <summary>变更类型</summary>
    public WatcherChangeTypes ChangeType { get; }

    /// <summary>旧路径（仅 Rename 时）</summary>
    public string? OldFilePath { get; }

    public HotReloadEventArgs(string filePath, WatcherChangeTypes changeType, string? oldFilePath = null)
    {
        FilePath = filePath;
        ChangeType = changeType;
        OldFilePath = oldFilePath;
    }
}
