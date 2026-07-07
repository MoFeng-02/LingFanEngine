namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 命令分发器接口 — 按命令类型路由到注册的处理器
/// <para>AOT 兼容：使用显式注册，不依赖反射。</para>
/// <para>线程安全：支持运行时注册自定义命令。</para>
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>注册命令处理器</summary>
    void Register<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand;

    /// <summary>注册命令处理器（Lambda 简化版）</summary>
    void Register<TCommand>(Action<TCommand, ICommandContext> handler) where TCommand : ICommand;

    /// <summary>分发命令到对应的处理器</summary>
    void Dispatch(ICommand command, ICommandContext ctx);

    /// <summary>是否已注册指定命令类型的处理器</summary>
    bool IsRegistered<TCommand>() where TCommand : ICommand;
}
