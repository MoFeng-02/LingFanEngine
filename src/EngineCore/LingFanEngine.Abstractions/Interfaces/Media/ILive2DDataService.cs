namespace LingFanEngine.Abstractions.Interfaces.Media;

/// <summary>
/// Live2D 模型数据接口
/// <para>服务层返回 Live2D 模型数据，供渲染层渲染</para>
/// </summary>
public interface ILive2DModelData
{
    /// <summary>
    /// 模型路径
    /// </summary>
    string Path { get; }

    /// <summary>
    /// 可用的表情 ID 列表
    /// </summary>
    IReadOnlyList<string> ExpressionIds { get; }

    /// <summary>
    /// 可用的动作 ID 列表
    /// </summary>
    IReadOnlyList<string> MotionIds { get; }

    /// <summary>
    /// 可用的参数 ID 列表
    /// </summary>
    IReadOnlyList<string> ParameterIds { get; }
}

/// <summary>
/// Live2D 数据服务接口
/// <para>服务层只负责返回 Live2D 模型数据，不负责实际渲染</para>
/// </summary>
public interface ILive2DDataService
{
    /// <summary>
    /// 获取 Live2D 模型数据
    /// </summary>
    /// <param name="path">模型路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>模型数据，不存在返回 null</returns>
    Task<ILive2DModelData?> GetModelDataAsync(string path, CancellationToken ct = default);
}