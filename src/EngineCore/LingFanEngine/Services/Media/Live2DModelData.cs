using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Services.Media;

/// <summary>
/// Live2D 模型数据实现
/// </summary>
public class Live2DModelData : ILive2DModelData
{
    public required string Path { get; init; }
    public IReadOnlyList<string> ExpressionIds { get; init; } = [];
    public IReadOnlyList<string> MotionIds { get; init; } = [];
    public IReadOnlyList<string> ParameterIds { get; init; } = [];
}