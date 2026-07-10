using System.Collections.Generic;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>平台服务抽象</summary>
public interface IPlatformService
{
    /// <summary>默认项目目录</summary>
    string GetDefaultProjectDirectory();

    /// <summary>应用数据目录</summary>
    string GetAppDataDirectory();

    /// <summary>在文件资源管理器中打开</summary>
    void OpenInFileExplorer(string path);

    /// <summary>在终端中打开</summary>
    void OpenInTerminal(string path);

    /// <summary>获取系统字体列表</summary>
    List<string> GetSystemFonts();
}
