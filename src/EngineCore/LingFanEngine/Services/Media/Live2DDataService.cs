using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// Live2D 数据服务实现
/// <para>只负责返回 Live2D 模型数据，不负责实际渲染</para>
/// </summary>
public class Live2DDataService : ILive2DDataService
{
    private readonly string _modelBasePath;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="modelBasePath">模型资源根目录</param>
    public Live2DDataService(string modelBasePath = "Live2D")
    {
        _modelBasePath = modelBasePath;
    }

    /// <inheritdoc/>
    public Task<ILive2DModelData?> GetModelDataAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
            return Task.FromResult<ILive2DModelData?>(null);

        // TODO: 从模型文件读取元数据（表情、动作、参数列表）
        var data = new Live2DModelData
        {
            Path = path,
            ExpressionIds = [], // 从模型文件读取
            MotionIds = [],     // 从模型文件读取
            ParameterIds = []  // 从模型文件读取
        };

        return Task.FromResult<ILive2DModelData?>(data);
    }

    /// <summary>
    /// 获取完整路径
    /// </summary>
    private string GetFullPath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.Combine(_modelBasePath, path);
    }
}