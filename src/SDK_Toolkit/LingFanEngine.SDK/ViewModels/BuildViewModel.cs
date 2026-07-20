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
    private readonly IEngineUpdateService _engineUpdateService;
    private readonly IRunService _runService;

    [ObservableProperty]
    private bool _targetWindows = true;

    [ObservableProperty]
    private bool _targetLinux;

    [ObservableProperty]
    private bool _targetMacOSArm64;

    [ObservableProperty]
    private bool _targetMacOSX64;

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

    // ===== 项目引擎依赖（版本隔离核心） =====
    // 当前项目 DLL/ 内的引擎版本（来自 engine.lock.json / LingFanEngine.dll 元数据）
    [ObservableProperty]
    private string _engineDependencyVersion = "—";

    // 项目引擎依赖更新状态文案
    [ObservableProperty]
    private string _projectEngineUpdateMessage = "";

    // 是否正在检查/应用项目引擎更新（控制按钮可用性）
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateProjectEngineCommand))]
    private bool _isUpdatingProjectEngine;

    // ===== 游戏运行（启动/停止） =====
    // 是否正在启动游戏（含未构建时自动构建）
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchGameCommand), nameof(BuildCommand))]
    private bool _isLaunching;

    // 是否正在停止游戏
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopGameCommand), nameof(LaunchGameCommand))]
    private bool _isStopping;

    // 启动/停止游戏状态文案
    [ObservableProperty]
    private string _launchMessage = "";

    public ObservableCollection<string> LogEntries { get; } = new();

    public BuildViewModel(IPublishService publishService, IProjectSession session,
        IEngineUpdateService engineUpdateService, IRunService runService)
    {
        _publishService = publishService;
        _session = session;
        _engineUpdateService = engineUpdateService;
        _runService = runService;

        // 监听项目会话
        _session.ProjectOpened += OnProjectOpened;
        _session.ProjectClosed += OnProjectClosed;

        // 初始检查
        if (_session.IsProjectOpen && _session.CurrentProject != null)
        {
            OnProjectOpened();
        }

        // 刷新项目引擎依赖版本展示（未打开项目时显示占位）
        RefreshProjectEngineVersion();

        // 属性变更时自动保存（防抖 500ms）
        PropertyChanged += OnViewModelPropertyChanged;
    }

    private CancellationTokenSource? _saveCts;
    private bool _isLoading; // 加载配置时抑制自动保存

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isLoading) return; // 加载中不触发保存

        // 只关注构建相关属性
        if (e.PropertyName is not (nameof(TargetWindows) or nameof(TargetLinux) or nameof(TargetMacOSArm64) or nameof(TargetMacOSX64)
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
        if (TargetMacOSArm64) platforms.Add(PlatformConfig.MacOS);
        if (TargetMacOSX64) platforms.Add(PlatformConfig.MacOSX64);
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
            TargetMacOSArm64 = project.TargetPlatforms.Exists(p => p.RuntimeIdentifier == "osx-arm64");
            TargetMacOSX64 = project.TargetPlatforms.Exists(p => p.RuntimeIdentifier == "osx-x64");

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

            // 刷新项目引擎依赖版本展示
            RefreshProjectEngineVersion();
        }
        finally
        {
            _isLoading = false;
        }

        // 项目开关影响命令可用性（是否可更新引擎依赖）
        UpdateProjectEngineCommand.NotifyCanExecuteChanged();
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

        // 引擎依赖版本回到占位，并禁用更新按钮
        EngineDependencyVersion = "—";
        ProjectEngineUpdateMessage = "";
        UpdateProjectEngineCommand.NotifyCanExecuteChanged();

        // 重置游戏运行状态
        LaunchMessage = "";
        IsLaunching = false;
        IsStopping = false;
        LaunchGameCommand.NotifyCanExecuteChanged();
        StopGameCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 刷新「项目引擎依赖版本」展示：优先读最终项目 engine.lock.json / DLL 元数据（版本真相在最终项目）；
    /// 未打开项目时显示占位。
    /// </summary>
    private void RefreshProjectEngineVersion()
    {
        if (!_session.IsProjectOpen || string.IsNullOrWhiteSpace(_session.ProjectDirectory))
        {
            EngineDependencyVersion = "—";
            return;
        }
        var ver = _engineUpdateService.GetProjectEngineVersion(_session.ProjectDirectory);
        EngineDependencyVersion = string.IsNullOrWhiteSpace(ver) ? "—" : ver;
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
            if (TargetMacOSArm64) platforms.Add(PlatformConfig.MacOS);
            if (TargetMacOSX64) platforms.Add(PlatformConfig.MacOSX64);

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

    private bool CanBuild => _session.IsProjectOpen && !IsBuilding && !IsLaunching;

    /// <summary>
    /// 检查并应用「项目引擎依赖」更新（GitHub Release）。
    /// <para>仅替换项目 DLL/ 内 4 个引擎 DLL（版本隔离点），逐 DLL 比对 + sha256 校验；
    /// 锁定/缺失 DLL 视为 0.0.0 触发更新，已最新或更新失败绝不降级。更新后回写 engine.lock.json。</para>
    /// <para>注意：更新仅替换开发态 DLL，需重新构建发布后新版本才会编入最终 exe。</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdateProjectEngine))]
    private async Task UpdateProjectEngineAsync()
    {
        var dir = _session.ProjectDirectory;
        if (!_session.IsProjectOpen || string.IsNullOrWhiteSpace(dir))
        {
            ProjectEngineUpdateMessage = "请先打开项目";
            return;
        }

        IsUpdatingProjectEngine = true;
        ProjectEngineUpdateMessage = "正在检查项目引擎依赖更新...";

        try
        {
            var progress = new Progress<string>(msg => ProjectEngineUpdateMessage = msg);
            var result = await _engineUpdateService.UpdateProjectAsync(dir, progress);

            ProjectEngineUpdateMessage = result.Status switch
            {
                EngineUpdateStatus.UpToDate => $"已是最新版本（{EngineDependencyVersion}）",
                EngineUpdateStatus.UpdateApplied => $"已更新到 {result.ManifestVersion}（替换 {result.UpdatedDlls.Count} 个 DLL，重新构建后生效）",
                EngineUpdateStatus.Failed => $"更新失败：{result.ErrorMessage}",
                _ => ProjectEngineUpdateMessage,
            };

            // 热替换成功后刷新版本展示
            if (result.Status == EngineUpdateStatus.UpdateApplied
                && !string.IsNullOrEmpty(result.ManifestVersion))
            {
                EngineDependencyVersion = result.ManifestVersion;
            }
        }
        catch (Exception ex)
        {
            ProjectEngineUpdateMessage = $"更新异常：{ex.Message}";
        }
        finally
        {
            IsUpdatingProjectEngine = false;
        }
    }

    private bool CanUpdateProjectEngine() => _session.IsProjectOpen && !IsUpdatingProjectEngine;

    // ===== 游戏运行：启动 / 停止 =====

    private void AppendBuildLog(string msg)
    {
        LogEntries.Add(msg);
        BuildLog += msg + "\n";
    }

    /// <summary>
    /// 启动游戏：若已构建则直接运行；未构建则自动构建当前平台后再启动。
    /// <para>与构建互斥（同一时刻只允许一个长时间操作）。</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLaunchGame))]
    private async Task LaunchGameAsync()
    {
        var project = _session.CurrentProject;
        if (project == null)
        {
            LaunchMessage = "请先打开项目";
            return;
        }

        IsLaunching = true;
        LaunchMessage = "正在定位游戏可执行文件...";
        var progress = new Progress<string>(msg => AppendBuildLog(msg));

        try
        {
            var result = await _runService.LaunchAsync(project, progress);
            LaunchMessage = result.Message;
        }
        catch (Exception ex)
        {
            LaunchMessage = $"启动异常：{ex.Message}";
        }
        finally
        {
            IsLaunching = false;
        }
    }

    private bool CanLaunchGame() =>
        _session.IsProjectOpen && !IsBuilding && !IsLaunching && !IsStopping;

    /// <summary>停止正在运行的游戏进程（防止后续构建/DLL 更新时文件被锁）</summary>
    [RelayCommand(CanExecute = nameof(CanStopGame))]
    private async Task StopGameAsync()
    {
        var project = _session.CurrentProject;
        if (project == null)
        {
            LaunchMessage = "请先打开项目";
            return;
        }

        IsStopping = true;
        LaunchMessage = "正在停止游戏进程...";
        try
        {
            await _runService.StopAsync(project);
            LaunchMessage = "已请求停止游戏进程（若正在运行）。";
        }
        catch (Exception ex)
        {
            LaunchMessage = $"停止异常：{ex.Message}";
        }
        finally
        {
            IsStopping = false;
        }
    }

    private bool CanStopGame() =>
        _session.IsProjectOpen && !IsStopping && !IsLaunching && !IsBuilding;
}
