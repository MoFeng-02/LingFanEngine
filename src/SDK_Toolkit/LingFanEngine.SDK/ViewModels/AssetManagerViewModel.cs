using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>资源管理 ViewModel</summary>
public partial class AssetManagerViewModel : ViewModelBase
{
    private readonly IAssetManager _assetManager;
    private readonly IProjectSession _session;
    private List<AssetEntry> _allAssets = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _watcherDebounce;

    [ObservableProperty]
    private ObservableCollection<AssetEntry> _assets = new();

    [ObservableProperty]
    private AssetEntry? _selectedAsset;

    [ObservableProperty]
    private AssetPreview? _preview;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private string _projectDirectory = "";

    /// <summary>资源目录路径（资源实际所在位置）</summary>
    [ObservableProperty]
    private string _resourcesDirectory = "";

    /// <summary>当前筛选的分类索引（0=全部, 1=故事, 2=图片, 3=音频, 4=视频, 5=其他）</summary>
    [ObservableProperty]
    private int _selectedCategoryIndex = 0;

    public AssetManagerViewModel(IAssetManager assetManager, IProjectSession session)
    {
        _assetManager = assetManager;
        _session = session;

        // 监听项目会话
        _session.ProjectOpened += OnProjectOpened;
        _session.ProjectClosed += OnProjectClosed;

        // 初始检查
        if (_session.IsProjectOpen)
        {
            OnProjectOpened();
        }
    }

    private void OnProjectOpened()
    {
        ProjectDirectory = _session.ProjectDirectory;
        ResourcesDirectory = _session.ResourcesDirectory;
        StartWatching();
        _ = ScanAssetsAsync();
    }

    private void OnProjectClosed()
    {
        StopWatching();
        ProjectDirectory = "";
        ResourcesDirectory = "";
        _allAssets.Clear();
        Assets.Clear();
        Preview = null;
        StatusMessage = "就绪";
    }

    /// <summary>启动对资源目录的文件监听（外部动态增删/改动文件时自动防抖重扫）。</summary>
    private void StartWatching()
    {
        StopWatching();
        var dir = ResourcesDirectory;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;
        try
        {
            _watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
            };
            _watcher.Changed += OnResourceChanged;
            _watcher.Created += OnResourceChanged;
            _watcher.Deleted += OnResourceChanged;
            _watcher.Renamed += OnResourceChanged;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AssetManager] 资源监听启动失败: {ex.Message}");
        }
    }

    private void StopWatching()
    {
        _watcherDebounce?.Cancel();
        _watcherDebounce = null;
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void OnResourceChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            e.FullPath.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
        ScheduleRefresh();
    }

    /// <summary>监听缓冲溢出（大量快速变更）→ 全量刷新兜底，保证正确性。</summary>
    private void OnWatcherError(object sender, ErrorEventArgs e) => ScheduleRefresh();

    /// <summary>防抖刷新（合并短时间内连续事件，回到 UI 线程触发扫描）。</summary>
    private void ScheduleRefresh()
    {
        _watcherDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _watcherDebounce = cts;
        var token = cts.Token;
        _ = Task.Delay(400, token).ContinueWith(t =>
        {
            if (token.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() => _ = ScanAssetsAsync());
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>扫描项目资源</summary>
    [RelayCommand]
    private async Task ScanAssetsAsync()
    {
        if (string.IsNullOrEmpty(ResourcesDirectory))
        {
            StatusMessage = "请先打开项目";
            return;
        }

        try
        {
            StatusMessage = "正在扫描资源...";
            var entries = await _assetManager.ScanAssetsAsync(ResourcesDirectory);
            _allAssets = entries.ToList();
            ApplyCategoryFilter();
            StatusMessage = $"找到 {entries.Count} 个资源";
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败: {ex.Message}";
        }
    }

    /// <summary>筛选分类</summary>
    [RelayCommand]
    private void FilterCategory(int categoryIndex)
    {
        SelectedCategoryIndex = categoryIndex;
        ApplyCategoryFilter();
    }

    /// <summary>根据当前选中的分类索引过滤资源列表</summary>
    private void ApplyCategoryFilter()
    {
        Assets.Clear();
        var filtered = SelectedCategoryIndex switch
        {
            0 => _allAssets,
            1 => _allAssets.Where(a => a.Type == AssetType.Story),
            2 => _allAssets.Where(a => a.Type == AssetType.Image),
            3 => _allAssets.Where(a => a.Type == AssetType.Audio),
            4 => _allAssets.Where(a => a.Type == AssetType.Video),
            5 => _allAssets.Where(a => a.Type is AssetType.Json or AssetType.Other),
            _ => _allAssets
        };
        foreach (var entry in filtered)
            Assets.Add(entry);
    }

    /// <summary>选中资源时获取预览</summary>
    partial void OnSelectedAssetChanged(AssetEntry? value)
    {
        if (value == null)
        {
            Preview = null;
            return;
        }

        _ = LoadPreviewAsync(value.Path);
    }

    private async Task LoadPreviewAsync(string path)
    {
        try
        {
            Preview = await _assetManager.GetPreviewAsync(path);
        }
        catch (Exception ex)
        {
            StatusMessage = $"预览失败: {ex.Message}";
        }
    }

    /// <summary>P2-5: 拖拽导入资源文件</summary>
    [RelayCommand]
    private async Task ImportFilesAsync(string[] filePaths)
    {
        if (string.IsNullOrEmpty(ResourcesDirectory))
        {
            StatusMessage = "请先打开项目";
            return;
        }

        var mediaDir = System.IO.Path.Combine(ResourcesDirectory, ProjectConstants.MediaDir);
        System.IO.Directory.CreateDirectory(mediaDir);

        var imported = 0;
        foreach (var filePath in filePaths)
        {
            if (!System.IO.File.Exists(filePath)) continue;

            try
            {
                var fileName = System.IO.Path.GetFileName(filePath);
                var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();

                // 根据扩展名确定目标子目录
                var targetSubDir = ProjectConstants.GetImportTargetSubDir(ext);

                await _assetManager.ImportAssetAsync(ResourcesDirectory, filePath, targetSubDir);
                imported++;
            }
            catch (Exception ex)
            {
                StatusMessage = $"导入失败 {System.IO.Path.GetFileName(filePath)}: {ex.Message}";
            }
        }

        if (imported > 0)
        {
            StatusMessage = $"已导入 {imported} 个资源";
            await ScanAssetsAsync();
        }
    }
}
