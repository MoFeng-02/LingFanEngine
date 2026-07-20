using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;
using System.Text.Json.Serialization.Metadata;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>设置 ViewModel（P2-4 增强）</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IPlatformService _platformService;
    private readonly IEngineUpdateService _engineUpdateService;
    private readonly IProjectSession _projectSession;
    private readonly ITemplateUpdateService _templateUpdateService;

    [ObservableProperty]
    private string _sdkVersion = "0.1.0";

    [ObservableProperty]
    private string _dotNetVersion = "";

    [ObservableProperty]
    private string _appDataDirectory = "";

    [ObservableProperty]
    private string _defaultProjectDirectory = "";

    [ObservableProperty]
    private string _statusMessage = "";

    // P2-4: 编辑器设置
    [ObservableProperty]
    private string _editorFontFamily = "Consolas";

    [ObservableProperty]
    private int _editorFontSize = 14;

    [ObservableProperty]
    private string _indentStyle = "spaces";

    [ObservableProperty]
    private int _indentWidth = 4;

    [ObservableProperty]
    private bool _formatOnSave;

    [ObservableProperty]
    private bool _showLineNumbers = true;

    [ObservableProperty]
    private bool _showMinimap;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    private string _theme = "dark";

    // P2-4: 构建设置
    [ObservableProperty]
    private string _defaultBuildConfig = "Release";

    [ObservableProperty]
    private bool _defaultSelfContained = true;

    [ObservableProperty]
    private bool _defaultPublishAot = true;

    // P2-4: 引擎版本（只读）——优先从 LingFanEngine.dll 元数据读取真实版本
    [ObservableProperty]
    private string _engineVersion = "1.0.0";

    // 引擎更新状态文案（UI 显示）
    [ObservableProperty]
    private string _engineUpdateMessage = "";

    // 是否正在检查/应用引擎更新（控制按钮可用性）
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckEngineUpdateCommand))]
    private bool _isCheckingEngineUpdate;

    // 模板版本（只读）——当前生效模板版本（缓存 lock 或内置基线）
    [ObservableProperty]
    private string _templateVersion = TemplateDefaults.BuiltinVersion;

    // 模板更新状态文案（UI 显示）
    [ObservableProperty]
    private string _templateUpdateMessage = "";

    // 是否正在检查/应用模板更新（控制按钮可用性）
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckTemplateUpdateCommand))]
    private bool _isCheckingTemplateUpdate;

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LingFanEngine", "sdk_settings.json");

    public SettingsViewModel(
        IPlatformService platformService,
        IEngineUpdateService engineUpdateService,
        IProjectSession projectSession,
        ITemplateUpdateService templateUpdateService)
    {
        _platformService = platformService;
        _engineUpdateService = engineUpdateService;
        _projectSession = projectSession;
        _templateUpdateService = templateUpdateService;

        // 初始化信息
        DotNetVersion = ProcessHelper.GetDotNetVersion() ?? "未安装";
        AppDataDirectory = PathHelper.GetAppDataDirectory();
        DefaultProjectDirectory = _platformService.GetDefaultProjectDirectory();

        // 加载持久化设置
        LoadSettings();

        // 引擎版本真相在最终项目 DLL/；未打开项目时回退到 SDK 种子版本
        RefreshEngineVersion();

        // 模板版本：读模板缓存 lock，无则回退内置基线
        TemplateVersion = _templateUpdateService.CurrentTemplateVersion;

        // 项目开关时，版本展示同步刷新
        _projectSession.ProjectOpened += () => RefreshEngineVersion();
        _projectSession.ProjectClosed += () => RefreshEngineVersion();
    }

    /// <summary>
    /// 刷新显示的引擎版本：优先读最终项目的真实版本；未打开项目时回退 SDK 种子版本。
    /// </summary>
    private void RefreshEngineVersion()
    {
        string? ver = null;
        if (_projectSession.IsProjectOpen &&
            !string.IsNullOrWhiteSpace(_projectSession.ProjectDirectory))
        {
            ver = _engineUpdateService.GetProjectEngineVersion(_projectSession.ProjectDirectory);
        }
        if (string.IsNullOrWhiteSpace(ver))
            ver = _engineUpdateService.CurrentEngineVersion; // SDK 种子兜底
        if (!string.IsNullOrWhiteSpace(ver) && ver != "0.0.0")
            EngineVersion = ver;
    }

    /// <summary>加载设置（AOT 安全：使用 SdkJsonContext）</summary>
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return;

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonHelper.Deserialize(json, SdkJsonContext.Default.SdkSettings);
            if (settings != null)
            {
                EditorFontFamily = settings.EditorFontFamily;
                EditorFontSize = settings.EditorFontSize;
                IndentStyle = settings.IndentStyle;
                IndentWidth = settings.IndentWidth;
                FormatOnSave = settings.FormatOnSave;
                ShowLineNumbers = settings.ShowLineNumbers;
                ShowMinimap = settings.ShowMinimap;
                WordWrap = settings.WordWrap;
                Theme = settings.Theme;
                DefaultBuildConfig = settings.DefaultBuildConfig;
                DefaultSelfContained = settings.DefaultSelfContained;
                DefaultPublishAot = settings.DefaultPublishAot;
                EngineVersion = settings.EngineVersion;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] 加载失败: {ex.Message}");
        }
    }

    /// <summary>保存设置（AOT 安全：使用 SdkJsonContext）</summary>
    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var settings = new SdkSettings
            {
                EditorFontFamily = EditorFontFamily,
                EditorFontSize = EditorFontSize,
                IndentStyle = IndentStyle,
                IndentWidth = IndentWidth,
                FormatOnSave = FormatOnSave,
                ShowLineNumbers = ShowLineNumbers,
                ShowMinimap = ShowMinimap,
                WordWrap = WordWrap,
                Theme = Theme,
                DefaultBuildConfig = DefaultBuildConfig,
                DefaultSelfContained = DefaultSelfContained,
                DefaultPublishAot = DefaultPublishAot,
                SdkVersion = SdkVersion,
                EngineVersion = EngineVersion,
                DotNetVersion = DotNetVersion,
            };

            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonHelper.Serialize(settings, SdkJsonContext.Default.SdkSettings);
            File.WriteAllText(SettingsFilePath, json);
            StatusMessage = "设置已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
    }

    /// <summary>在资源管理器中打开应用数据目录</summary>
    [RelayCommand]
    private void OpenAppData()
    {
        _platformService.OpenInFileExplorer(AppDataDirectory);
    }

    /// <summary>检查 dotnet 安装</summary>
    [RelayCommand]
    private void CheckDotNet()
    {
        if (ProcessHelper.CheckDotNetInstalled())
        {
            StatusMessage = $"dotnet 已安装: {DotNetVersion}";
        }
        else
        {
            StatusMessage = "dotnet 未安装或不在 PATH 中";
        }
    }

    /// <summary>
    /// 检查并应用引擎 DLL 更新（GitHub Release）。
    /// <para>检查到新版本时自动下载、sha256 校验、更新 SDK 自带 DLL 缓存；
    /// 被锁定的 DLL 写入 pending，提示重启 SDK 生效。</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckEngineUpdate))]
    private async Task CheckEngineUpdateAsync()
    {
        IsCheckingEngineUpdate = true;
        EngineUpdateMessage = "正在检查引擎更新...";
        StatusMessage = EngineUpdateMessage;

        try
        {
            var progress = new Progress<string>(msg =>
            {
                EngineUpdateMessage = msg;
                StatusMessage = msg;
            });

            var result = await _engineUpdateService.UpdateSdkCacheAsync(progress);

            EngineUpdateMessage = result.Status switch
            {
                EngineUpdateStatus.UpToDate => $"已是最新版本（{EngineVersion}）",
                EngineUpdateStatus.UpdateApplied => $"已更新到 {result.ManifestVersion}（热替换 {result.UpdatedDlls.Count} 个 DLL）",
                EngineUpdateStatus.PendingRestart => $"已更新到 {result.ManifestVersion}（热替换 {result.UpdatedDlls.Count}，{result.PendingDlls.Count} 个需重启 SDK 生效）",
                EngineUpdateStatus.Failed => $"更新失败：{result.ErrorMessage}",
                _ => EngineUpdateMessage,
            };
            StatusMessage = EngineUpdateMessage;

            // 热替换成功后刷新显示的引擎版本
            if (result.Status is EngineUpdateStatus.UpdateApplied or EngineUpdateStatus.PendingRestart
                && !string.IsNullOrEmpty(result.ManifestVersion))
            {
                EngineVersion = result.ManifestVersion;
            }
        }
        catch (Exception ex)
        {
            EngineUpdateMessage = $"检查失败：{ex.Message}";
            StatusMessage = EngineUpdateMessage;
        }
        finally
        {
            IsCheckingEngineUpdate = false;
        }
    }

    private bool CanCheckEngineUpdate() => !IsCheckingEngineUpdate;

    /// <summary>
    /// 检查并应用模板更新（GitHub Release 模板 zip）。
    /// <para>检查到新版本时自动下载、sha256 校验、解压覆盖本地模板缓存；
    /// 之后新建项目将优先使用更新后的模板。</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckTemplateUpdate))]
    private async Task CheckTemplateUpdateAsync()
    {
        IsCheckingTemplateUpdate = true;
        TemplateUpdateMessage = "正在检查模板更新...";
        StatusMessage = TemplateUpdateMessage;

        try
        {
            var progress = new Progress<string>(msg =>
            {
                TemplateUpdateMessage = msg;
                StatusMessage = msg;
            });

            var result = await _templateUpdateService.UpdateTemplateAsync(progress);

            TemplateUpdateMessage = result.Status switch
            {
                TemplateUpdateStatus.UpToDate => $"模板已是最新（{TemplateVersion}）",
                TemplateUpdateStatus.UpdateApplied => $"模板已更新到 {result.ManifestVersion}",
                TemplateUpdateStatus.Failed => $"模板更新失败：{result.ErrorMessage}",
                _ => TemplateUpdateMessage,
            };
            StatusMessage = TemplateUpdateMessage;

            if (result.Status == TemplateUpdateStatus.UpdateApplied && !string.IsNullOrEmpty(result.ManifestVersion))
            {
                TemplateVersion = result.ManifestVersion;
            }
        }
        catch (Exception ex)
        {
            TemplateUpdateMessage = $"检查模板失败：{ex.Message}";
            StatusMessage = TemplateUpdateMessage;
        }
        finally
        {
            IsCheckingTemplateUpdate = false;
        }
    }

    private bool CanCheckTemplateUpdate() => !IsCheckingTemplateUpdate;
}
