using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>资源管理 ViewModel</summary>
public partial class AssetManagerViewModel : ViewModelBase
{
    private readonly IAssetManager _assetManager;
    private readonly IProjectSession _session;

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
        _ = ScanAssetsAsync();
    }

    private void OnProjectClosed()
    {
        ProjectDirectory = "";
        Assets.Clear();
        Preview = null;
        StatusMessage = "就绪";
    }

    /// <summary>扫描项目资源</summary>
    [RelayCommand]
    private async Task ScanAssetsAsync()
    {
        if (string.IsNullOrEmpty(ProjectDirectory))
        {
            StatusMessage = "请先打开项目";
            return;
        }

        try
        {
            StatusMessage = "正在扫描资源...";
            var entries = await _assetManager.ScanAssetsAsync(ProjectDirectory);
            Assets.Clear();
            foreach (var entry in entries)
                Assets.Add(entry);
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
        // TODO: 根据分类过滤 Assets 列表
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
}
