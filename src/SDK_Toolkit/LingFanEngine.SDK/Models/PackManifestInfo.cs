namespace LingFanEngine.SDK.Models;

/// <summary>
/// .lfpack 包清单信息（SDK 层模型，独立于引擎核心）
/// </summary>
public class PackManifestInfo
{
    /// <summary>包名称</summary>
    public string Name { get; set; } = "";

    /// <summary>包版本号</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>包内文件路径列表</summary>
    public List<string> Files { get; set; } = [];

    /// <summary>引擎版本</summary>
    public string EngineVersion { get; set; } = "";

    /// <summary>包描述</summary>
    public string? Description { get; set; }
}

/// <summary>
/// 打包/解包结果
/// </summary>
public class PackResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputPath { get; set; }
    public int FileCount { get; set; }
    public List<string> Logs { get; set; } = [];
}
