namespace LingFanEngine.Abstractions.Models;

public class BaseModel
{
    /// <summary>
    /// Key
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// 修改时间
    /// </summary>
    public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.UtcNow;
}
