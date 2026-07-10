using System;
using System.Collections.ObjectModel;
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
    private bool _enableEncryption = true;

    [ObservableProperty]
    private int _keyShardCount = 4;

    [ObservableProperty]
    private bool _publishAot = true;

    [ObservableProperty]
    private string _buildLog = "";

    [ObservableProperty]
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
    }

    private void OnProjectOpened()
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
        }

        PublishAot = project.Build.PublishAot;
    }

    private void OnProjectClosed()
    {
        ProjectName = "(未打开项目)";
        ProjectPath = "";
        LogEntries.Clear();
        BuildLog = "";
        BuildProgress = 0;
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
            project.TargetPlatforms = platforms;
            if (project.Encryption != null)
            {
                project.Encryption.Enabled = EnableEncryption;
                project.Encryption.KeyShardCount = KeyShardCount;
            }
            project.Build.PublishAot = PublishAot;

            // 逐平台构建
            foreach (var platform in platforms)
            {
                Log($"正在构建 {platform.Name} (RID: {platform.RuntimeIdentifier})...");
                var result = await _publishService.BuildAsync(project, platform, progress);
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
