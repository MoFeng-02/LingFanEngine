using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.I18n;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Constants;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;
using System.Text.Json.Serialization.Metadata;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>语言选项（原生名显示）</summary>
public sealed record LanguageOption(string Code, string Label);

/// <summary>设置 ViewModel（P2-4 增强）</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IPlatformService _platformService;
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

    // 构建设置
    [ObservableProperty]
    private string _defaultBuildConfig = "Release";

    [ObservableProperty]
    private bool _defaultSelfContained = true;

    [ObservableProperty]
    private bool _defaultPublishAot = true;

    // 界面语言
    /// <summary>可选界面语言（原生名）</summary>
    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("zh-Hans", "中文"),
        new("en-US", "English"),
        new("ja-JP", "日本語"),
    ];

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    /// <summary>语言切换即生效：设置资源文化 + 立即保存（当前已渲染界面需重启或重建窗口才整体刷新）。</summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null) return;
        SdkLocalizer.SetCulture(value.Code);
        SaveSettings();
        StatusMessage = SdkLocalizer.Loc("Set_LangSwitched", value.Label);
    }

    // 引擎版本（只读）——当前打开项目 csproj 的 LingFanEngine PackageReference 版本
    // （2026-09 起引擎经 NuGet 分发，升级在项目里走 NuGet，SDK 不再热更引擎 DLL）
    [ObservableProperty]
    private string _engineVersion = "—";

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
        IProjectSession projectSession,
        ITemplateUpdateService templateUpdateService)
    {
        _platformService = platformService;
        _projectSession = projectSession;
        _templateUpdateService = templateUpdateService;

        // 初始化信息
        DotNetVersion = ProcessHelper.GetDotNetVersion() ?? SdkLocalizer.Loc("Set_DotNetMissing");
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
    /// 刷新显示的引擎版本：读当前打开项目 csproj 的 PackageReference（NuGet 分发后的版本真相）；
    /// 未打开项目时显示占位（也可被持久化的上次已知值覆盖）。
    /// </summary>
    private void RefreshEngineVersion()
    {
        if (_projectSession.IsProjectOpen &&
            !string.IsNullOrWhiteSpace(_projectSession.ProjectDirectory))
        {
            var ver = EnginePackageHelper.GetEnginePackageVersion(_projectSession.ProjectDirectory);
            if (!string.IsNullOrWhiteSpace(ver))
                EngineVersion = ver;
        }
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
                DefaultBuildConfig = settings.DefaultBuildConfig;
                DefaultSelfContained = settings.DefaultSelfContained;
                DefaultPublishAot = settings.DefaultPublishAot;
                EngineVersion = settings.EngineVersion;
                SelectedLanguage = MatchLanguage(settings.UILanguage);
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
                UILanguage = SelectedLanguage?.Code ?? "zh-Hans",
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
            StatusMessage = SdkLocalizer.Loc("Set_Saved");
        }
        catch (Exception ex)
        {
            StatusMessage = SdkLocalizer.Loc("Set_SaveFail", ex.Message);
        }
    }

    /// <summary>按语言码匹配选项（未知码回退到中文默认项）。</summary>
    private static LanguageOption? MatchLanguage(string code)
    {
        var options = new List<LanguageOption>
        {
            new("zh-Hans", "中文"),
            new("en-US", "English"),
            new("ja-JP", "日本語"),
        };
        for (var i = 0; i < options.Count; i++)
            if (string.Equals(code, options[i].Code, StringComparison.OrdinalIgnoreCase))
                return options[i];
        return options[0];
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
            StatusMessage = SdkLocalizer.Loc("Set_DotNetOk", DotNetVersion);
        }
        else
        {
            StatusMessage = SdkLocalizer.Loc("Set_DotNetNo");
        }
    }

    /// <summary>
    /// 检查并应用模板更新（GitHub Release 模板 zip）。
    /// <para>检查到新版本时自动下载、sha256 校验、解压覆盖本地模板缓存；
    /// 之后新建项目将优先使用更新后的模板。</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCheckTemplateUpdate))]
    private async Task CheckTemplateUpdateAsync()
    {
        IsCheckingTemplateUpdate = true;
        TemplateUpdateMessage = SdkLocalizer.Loc("Set_TplChecking");
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
                TemplateUpdateStatus.UpToDate => SdkLocalizer.Loc("Set_TplLatest", TemplateVersion),
                TemplateUpdateStatus.UpdateApplied => SdkLocalizer.Loc("Set_TplApplied", result.ManifestVersion),
                TemplateUpdateStatus.Failed => SdkLocalizer.Loc("Set_TplFail", result.ErrorMessage),
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
            TemplateUpdateMessage = SdkLocalizer.Loc("Set_TplExc", ex.Message);
            StatusMessage = TemplateUpdateMessage;
        }
        finally
        {
            IsCheckingTemplateUpdate = false;
        }
    }

    private bool CanCheckTemplateUpdate() => !IsCheckingTemplateUpdate;
}
