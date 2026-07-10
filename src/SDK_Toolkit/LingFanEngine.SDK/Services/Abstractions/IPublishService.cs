using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>构建发布服务</summary>
public interface IPublishService
{
    /// <summary>构建单个平台</summary>
    Task<BuildResult> BuildAsync(ProjectConfig project, PlatformConfig platform, IProgress<string>? progress = null);

    /// <summary>构建所有目标平台</summary>
    Task<List<BuildResult>> BuildAllAsync(ProjectConfig project, IProgress<string>? progress = null);

    /// <summary>获取支持的平台列表</summary>
    IReadOnlyList<PlatformConfig> GetSupportedPlatforms();
}
