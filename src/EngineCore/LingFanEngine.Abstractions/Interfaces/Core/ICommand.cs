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
/// 携带源文件信息的命令标记接口
/// <para>executor 在分发/执行每条命令前，将 <see cref="SourceFile"/> 写入状态键
/// <c>__current_file</c>，使 let/local 的局部作用域键能带上文件维度（同文件作用域隔离）。</para>
/// </summary>
public interface IFileScopedCommand : ICommand
{
    /// <summary>该命令来源的 .story 文件路径（编译期已知）</summary>
    string? SourceFile { get; }
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
