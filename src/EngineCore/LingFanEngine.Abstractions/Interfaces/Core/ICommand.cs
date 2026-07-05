namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 不可变命令接口
/// <para>所有状态变更封装为此接口的实现，经 Channel 投递到主循环消费。</para>
/// </summary>
public interface ICommand
{
    /// <summary>
    /// 命令创建时间戳
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// 命令优先级（数值越大优先级越高）
    /// </summary>
    CommandPriority Priority { get; }
}

/// <summary>
/// 命令优先级
/// </summary>
public enum CommandPriority
{
    /// <summary>背景逻辑</summary>
    Background = 0,
    /// <summary>普通逻辑</summary>
    Normal = 1,
    /// <summary>高优先级逻辑</summary>
    High = 2,
    /// <summary>渲染相关（最高）</summary>
    Render = 3
}
