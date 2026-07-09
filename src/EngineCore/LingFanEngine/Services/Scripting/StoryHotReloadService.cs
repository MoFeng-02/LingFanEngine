using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Services.Scripting;

/// <summary>
/// 故事文件热重载服务
/// <para>使用 FileSystemWatcher 监控 Stories 目录，检测到 .story 文件变更时自动重编译。</para>
/// <para>如果当前场景被重载，自动刷新 SceneView 并显示通知。</para>
/// <para>仅在 LingFanEngineOptions.EnableHotReload = true 时生效。</para>
/// </summary>
public class StoryHotReloadService : IDisposable
{
    private readonly IStoryRegistry _storyRegistry;
    private readonly IStateContainer _state;
    private readonly ICommandPipeline _pipeline;
    private readonly LingFanEngineOptions _options;
    private FileSystemWatcher? _watcher;
    private readonly Dictionary<string, DateTime> _lastReloadTimes = new(StringComparer.OrdinalIgnoreCase);
    private bool _started;

    public StoryHotReloadService(
        IStoryRegistry storyRegistry,
        IStateContainer state,
        ICommandPipeline pipeline,
        LingFanEngineOptions options)
    {
        _storyRegistry = storyRegistry;
        _state = state;
        _pipeline = pipeline;
        _options = options;
    }

    /// <summary>
    /// 启动热重载监视
    /// </summary>
    public void Start()
    {
        if (_started || !_options.EnableHotReload) return;
        _started = true;

        var storyRoot = ResolveStoryRoot();
        if (storyRoot == null)
        {
            System.Diagnostics.Debug.WriteLine("[StoryHotReload] Stories 目录未找到，热重载未启动");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher
            {
                Path = storyRoot,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                InternalBufferSize = 65536,
                Filter = "*.story"
            };

            _watcher.Changed += OnStoryFileChanged;
            _watcher.Created += OnStoryFileChanged;

            System.Diagnostics.Debug.WriteLine($"[StoryHotReload] 热重载已启动，监视目录: {storyRoot}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StoryHotReload] 启动失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 停止热重载监视
    /// </summary>
    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _started = false;
    }

    /// <summary>
    /// .story 文件变更处理
    /// </summary>
    private void OnStoryFileChanged(object sender, FileSystemEventArgs e)
    {
        // 防抖：同一文件 500ms 内只触发一次重载
        var now = DateTime.UtcNow;
        if (_lastReloadTimes.TryGetValue(e.FullPath, out var lastTime) &&
            (now - lastTime).TotalMilliseconds < 500)
            return;
        _lastReloadTimes[e.FullPath] = now;

        // 延迟一小段时间，确保文件写入完成
        Task.Run(async () =>
        {
            await Task.Delay(200);
            try
            {
                ReloadStoryFile(e.FullPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StoryHotReload] 重载失败: {e.FullPath} -> {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 重载故事文件并刷新当前场景（如果受影响）
    /// </summary>
    private void ReloadStoryFile(string filePath)
    {
        System.Diagnostics.Debug.WriteLine($"[StoryHotReload] 重载文件: {filePath}");

        var affectedScenes = _storyRegistry.ReloadFile(filePath);
        if (affectedScenes.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[StoryHotReload] 无受影响场景");
            return;
        }

        // 检查当前场景是否受影响
        var currentScene = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";

        if (affectedScenes.Contains(currentScene, StringComparer.OrdinalIgnoreCase))
        {
            // 当前场景被重载——通过 Navigate 刷新
            System.Diagnostics.Debug.WriteLine($"[StoryHotReload] 当前场景 [{currentScene}] 被重载，刷新 SceneView");

            // 在 UI 线程上发送导航命令刷新场景
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _pipeline.SendAsync(new NavigateCommand { Path = currentScene });
                _state.Set(StateKeys.Notify.Text, $"🔄 已热重载: {Path.GetFileName(filePath)}");
            });
        }
        else
        {
            // 仅显示通知
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _state.Set(StateKeys.Notify.Text, $"🔄 故事文件已重载: {Path.GetFileName(filePath)}");
            });
        }
    }

    /// <summary>
    /// 解析故事根目录路径
    /// </summary>
    private string? ResolveStoryRoot()
    {
        // 1. 直接存在
        if (Directory.Exists(_options.StoriesDirectory))
            return _options.StoriesDirectory;

        // 2. 输出目录下
        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            var outputDir = Path.Combine(baseDir, _options.StoriesDirectory);
            if (Directory.Exists(outputDir))
                return outputDir;
        }

        // 3. 当前工作目录向上搜索
        var current = Path.GetFullPath(".");
        for (int i = 0; i < 5; i++)
        {
            var probe = Path.Combine(current, _options.StoriesDirectory);
            if (Directory.Exists(probe))
                return probe;
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Stop();
    }
}
