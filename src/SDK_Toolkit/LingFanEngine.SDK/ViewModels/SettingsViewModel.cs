using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingFanEngine.SDK.Services.Abstractions;
using LingFanEngine.SDK.Utils;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>设置 ViewModel</summary>
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

    public SettingsViewModel(IPlatformService platformService)
    {
        _platformService = platformService;

        // 初始化信息
        DotNetVersion = ProcessHelper.GetDotNetVersion() ?? "未安装";
        AppDataDirectory = PathHelper.GetAppDataDirectory();
        DefaultProjectDirectory = _platformService.GetDefaultProjectDirectory();
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
