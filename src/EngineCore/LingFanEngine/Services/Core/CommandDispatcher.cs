using System.Collections.Concurrent;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 命令分发器 — 按命令类型路由到注册的处理器
/// <para>AOT 兼容性说明：</para>
/// <para>• Register&lt;TCommand&gt; 使用 typeof(TCommand) 作为 key，编译时已知类型</para>
/// <para>• Dispatch 使用 command.GetType() 查找，运行时返回的 Type 对象与 typeof(T) 是同一引用</para>
/// <para>• ConcurrentDictionary 默认使用 ReferenceEquals 比较 Type，不依赖反射</para>
/// <para>• RegisterDefaultHandlers 使用显式 switch 类型匹配，完全 AOT 安全</para>
/// <para>线程安全：使用 ConcurrentDictionary 支持运行时注册自定义命令。</para>
/// </summary>
public class CommandDispatcher : ICommandDispatcher
{
    private readonly ConcurrentDictionary<Type, Action<ICommand, ICommandContext>> _handlers = new();

    /// <summary>
    /// 注册命令处理器
    /// </summary>
    /// <typeparam name="TCommand">命令类型</typeparam>
    /// <param name="handler">处理器实例</param>
    public void Register<TCommand>(ICommandHandler<TCommand> handler) where TCommand : ICommand
    {
        var type = typeof(TCommand);
        _handlers[type] = (cmd, ctx) => handler.Handle((TCommand)cmd, ctx);
    }

    /// <summary>
    /// 注册命令处理器（Lambda 简化版）
    /// </summary>
    public void Register<TCommand>(Action<TCommand, ICommandContext> handler) where TCommand : ICommand
    {
        var type = typeof(TCommand);
        _handlers[type] = (cmd, ctx) => handler((TCommand)cmd, ctx);
    }

    /// <summary>
    /// 分发命令到对应的处理器
    /// <para>未注册的命令类型将记录警告并忽略。</para>
    /// </summary>
    public void Dispatch(ICommand command, ICommandContext ctx)
    {
        var type = command.GetType();
        if (_handlers.TryGetValue(type, out var handler))
        {
            handler(command, ctx);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[CommandDispatcher] 未注册的命令类型: {type.Name}");
        }
    }

    /// <summary>
    /// 是否已注册指定命令类型的处理器
    /// </summary>
    public bool IsRegistered<TCommand>() where TCommand : ICommand
        => _handlers.ContainsKey(typeof(TCommand));
}
