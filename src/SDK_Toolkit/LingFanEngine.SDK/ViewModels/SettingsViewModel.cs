using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;
using System.Text.Json.Serialization.Metadata;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>设置 ViewModel（P2-4 增强）</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IPlatformService _platformService;

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

    // P2-4: 引擎版本（只读）
    [ObservableProperty]
    private string _engineVersion = "1.0.0";

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LingFanEngine", "sdk_settings.json");

    public SettingsViewModel(IPlatformService platformService)
    {
        _platformService = platformService;

        // 初始化信息
        DotNetVersion = ProcessHelper.GetDotNetVersion() ?? "未安装";
        AppDataDirectory = PathHelper.GetAppDataDirectory();
        DefaultProjectDirectory = _platformService.GetDefaultProjectDirectory();

        // 加载持久化设置
        LoadSettings();
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
        catch
        {
            // 加载失败——使用默认值
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
}
