using System.Collections.Generic;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>资源管理服务</summary>
public interface IAssetManager
{
    /// <summary>扫描项目资源</summary>
    Task<List<AssetEntry>> ScanAssetsAsync(string projectDir);

    /// <summary>导入资源到项目</summary>
    Task ImportAssetAsync(string projectDir, string sourceFile, string targetSubDir);

    /// <summary>删除资源</summary>
    Task DeleteAssetAsync(string assetPath);

    /// <summary>获取资源预览</summary>
    Task<AssetPreview?> GetPreviewAsync(string assetPath);
}
