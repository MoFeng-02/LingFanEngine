using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>构建发布 ViewModel</summary>
public partial class BuildViewModel : ViewModelBase
{
    private readonly IPublishService _publishService;
    private readonly IProjectSession _session;

    [ObservableProperty]
    private bool _targetWindows = true;

    [ObservableProperty]
    private bool _targetLinux;

    [ObservableProperty]
    private bool _targetMacOS;

    [ObservableProperty]
    private bool _enableEncryption = false;

    [ObservableProperty]
    private bool _encryptResources = false;

    [ObservableProperty]
    private int _keyShardCount = 4;

    [ObservableProperty]
    private bool _publishAot = true;

    /// <summary>可加密文件类型勾选列表</summary>
    public ObservableCollection<FileTypeCheckItem> EncryptFileTypes { get; } = new();

    [ObservableProperty]
    private string _buildLog = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private bool _isBuilding;

    [ObservableProperty]
    private double _buildProgress;

    [ObservableProperty]
    private string _projectName = "(未打开项目)";

    [ObservableProperty]
    private string _projectPath = "";

    public ObservableCollection<string> LogEntries { get; } = new();

    public BuildViewModel(IPublishService publishService, IProjectSession session)
    {
        _publishService = publishService;
        _session = session;

        // 监听项目会话
        _session.ProjectOpened += OnProjectOpened;
        _session.ProjectClosed += OnProjectClosed;

        // 初始检查
        if (_session.IsProjectOpen && _session.CurrentProject != null)
        {
            OnProjectOpened();
        }

        // 属性变更时自动保存（防抖 500ms）
        PropertyChanged += OnViewModelPropertyChanged;
    }

    private CancellationTokenSource? _saveCts;
    private bool _isLoading; // 加载配置时抑制自动保存

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isLoading) return; // 加载中不触发保存

        // 只关注构建相关属性
        if (e.PropertyName is not (nameof(TargetWindows) or nameof(TargetLinux) or nameof(TargetMacOS)
            or nameof(EnableEncryption) or nameof(EncryptResources) or nameof(PublishAot) or nameof(KeyShardCount)))
            return;

        ScheduleSave();
    }

    /// <summary>防抖保存——500ms 内无新变更才写入磁盘</summary>
    private void ScheduleSave()
    {
        if (!_session.IsProjectOpen || _session.CurrentProject == null) return;

        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        _ = SaveDebouncedAsync(_saveCts.Token);
    }

    private async Task SaveDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            WriteSettingsToProject();
            await _session.SaveCurrentProjectAsync();
        }
        catch (TaskCanceledException) { }
    }

    /// <summary>将 ViewModel 属性写回 ProjectConfig</summary>
    private void WriteSettingsToProject()
    {
        var project = _session.CurrentProject;
        if (project == null) return;

        // 平台
        var platforms = new System.Collections.Generic.List<PlatformConfig>();
        if (TargetWindows) platforms.Add(PlatformConfig.Windows);
        if (TargetLinux) platforms.Add(PlatformConfig.Linux);
        if (TargetMacOS) platforms.Add(PlatformConfig.MacOS);
        project.TargetPlatforms = platforms;

        // 加密
        if (project.Encryption != null)
        {
            project.Encryption.Enabled = EnableEncryption;
            project.Encryption.KeyShardCount = KeyShardCount;
            project.Encryption.EncryptFileTypes = EncryptFileTypes
                .Where(t => t.IsChecked)
                .Select(t => t.Extension)
                .ToList();
        }

        // 构建
        project.Build.EncryptResources = EncryptResources;
        project.Build.PublishAot = PublishAot;
    }

    private void OnProjectOpened()
    {
        _isLoading = true;
        try
        {
            var project = _session.CurrentProject;
            if (project == null) return;

            ProjectName = project.Title;
            ProjectPath = project.ProjectDirectory;

            // 从项目配置加载构建设置
            TargetWindows = project.TargetPlatforms.Exists(p => p.Name == "Windows");
            TargetLinux = project.TargetPlatforms.Exists(p => p.Name == "Linux");
            TargetMacOS = project.TargetPlatforms.Exists(p => p.Name == "macOS");

            if (project.Encryption != null)
            {
                EnableEncryption = project.Encryption.Enabled;
                KeyShardCount = project.Encryption.KeyShardCount;

                // 初始化文件类型勾选
                EncryptFileTypes.Clear();
                var selectedTypes = project.Encryption.EncryptFileTypes;
                foreach (var ext in EncryptionConfig.AllEncryptableTypes)
                {
                    var isChecked = selectedTypes == null || selectedTypes.Contains(ext);
                    var item = new FileTypeCheckItem(ext, isChecked);
                    item.PropertyChanged += (_, _) => ScheduleSave();
                    EncryptFileTypes.Add(item);
                }
            }

            EncryptResources = project.Build.EncryptResources;
            PublishAot = project.Build.PublishAot;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnProjectClosed()
    {
        ProjectName = "(未打开项目)";
        ProjectPath = "";
        LogEntries.Clear();
        BuildLog = "";
        BuildProgress = 0;
        EncryptFileTypes.Clear();
        EnableEncryption = false;
        EncryptResources = false;
    }

    /// <summary>开始构建</summary>
    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        var project = _session.CurrentProject;
        if (project == null)
        {
            LogEntries.Add("请先打开项目");
            return;
        }

        IsBuilding = true;
        BuildProgress = 0;
        LogEntries.Clear();
        BuildLog = "";

        void Log(string msg)
        {
            LogEntries.Add(msg);
            BuildLog += msg + "\n";
        }

        try
        {
            Log("=== 开始构建 ===");

            var progress = new Progress<string>(msg =>
            {
                Log(msg);
                BuildProgress = Math.Min(BuildProgress + 5, 90);
            });

            // 构建选中的平台
            var platforms = new System.Collections.Generic.List<PlatformConfig>();
            if (TargetWindows) platforms.Add(PlatformConfig.Windows);
            if (TargetLinux) platforms.Add(PlatformConfig.Linux);
            if (TargetMacOS) platforms.Add(PlatformConfig.MacOS);

            if (platforms.Count == 0)
            {
                Log("错误: 未选择目标平台");
                IsBuilding = false;
                return;
            }

            // 更新项目配置
            WriteSettingsToProject();
            // 构建前持久化
            await _session.SaveCurrentProjectAsync();

            // 逐平台构建（在线程池执行，避免同步 I/O 卡顿 UI）
            foreach (var platform in platforms)
            {
                Log($"正在构建 {platform.Name} (RID: {platform.RuntimeIdentifier})...");
                var result = await Task.Run(() => _publishService.BuildAsync(project, platform, progress));
                if (result.Success)
                {
                    Log($"[OK] {platform.Name} 构建成功: {result.OutputPath}");
                }
                else
                {
                    Log($"[FAIL] {platform.Name} 构建失败: {result.ErrorMessage}");
                }
            }

            BuildProgress = 100;
            Log("=== 构建完成 ===");
        }
        catch (Exception ex)
        {
            Log($"构建异常: {ex.Message}");
        }
        finally
        {
            IsBuilding = false;
        }
    }

    private bool CanBuild => _session.IsProjectOpen && !IsBuilding;
}
