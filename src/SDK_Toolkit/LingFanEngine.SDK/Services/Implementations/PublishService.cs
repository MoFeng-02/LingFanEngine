using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;
using LingFanEngine.SDK.Services.Abstractions;

namespace LingFanEngine.SDK.Services.Implementations;

/// <summary>
/// 构建发布服务实现——发布流程已阶段化为 <see cref="BuildPipeline"/>（探测/预处理/编译/加密+清单），
/// 本类仅作为入口把构建请求交付给管线，并保留多平台/平台能力查询。
/// </summary>
public class PublishService : IPublishService
{
    /// <summary>默认构造（无 PackTool）</summary>
    public PublishService() { }

    /// <summary>注入 PackTool 的构造（兼容旧 .lfpack 模式）</summary>
    public PublishService(IPackToolService packToolService) { }

    /// <inheritdoc/>
    public Task<BuildResult> BuildAsync(
        ProjectConfig project, PlatformConfig platform, IProgress<string>? progress = null)
    {
        var pipeline = new BuildPipeline(
            new DetectStage(),
            new PreprocessStage(),
            new PublishStage(),
            new EncryptStage());
        return pipeline.RunAsync(project, platform, progress);
    }

    /// <inheritdoc/>
    public async Task<List<BuildResult>> BuildAllAsync(
        ProjectConfig project, IProgress<string>? progress = null)
    {
        var results = new List<BuildResult>();
        foreach (var platform in project.TargetPlatforms)
        {
            var result = await BuildAsync(project, platform, progress).ConfigureAwait(false);
            results.Add(result);
        }
        return results;
    }

    /// <inheritdoc/>
    public IReadOnlyList<PlatformConfig> GetSupportedPlatforms()
    {
        return PlatformConfig.DesktopPlatforms;
    }
}