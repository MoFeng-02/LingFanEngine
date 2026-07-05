using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.Interfaces.Entry;

/// <summary>
/// 命令服务接口
/// <para>精简接口：命令通过管道投递到主循环；事件通过 Subscribe/Publish 注册/发布。</para>
/// <para>字符串命令兼容层保留，用于 BaseEntity.Command 字段的消费。</para>
/// </summary>
public interface ICommandService
{
    /// <summary>
    /// 投递命令到管道（推荐方式）
    /// </summary>
    ValueTask SendCommandAsync(ICommand command, CancellationToken ct = default);

    /// <summary>
    /// 注册字符串命令处理器（兼容 BaseEntity.Command 字段）
    /// </summary>
    void RegisterCommand(string commandName, Func<object?, CancellationToken, Task> handler);

    /// <summary>
    /// 执行字符串命令（兼容 BaseEntity.Command 字段）
    /// </summary>
    Task ExecuteAsync(string commandName, object? commandValue, CancellationToken ct = default);

    /// <summary>
    /// 订阅事件
    /// </summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;

    /// <summary>
    /// 发布事件（同步）
    /// </summary>
    void Publish<TEvent>(TEvent evt) where TEvent : class;

    /// <summary>
    /// 发布事件（异步）
    /// </summary>
    Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class;
}
