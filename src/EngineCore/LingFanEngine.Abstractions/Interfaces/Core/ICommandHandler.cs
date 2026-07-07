namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 命令处理器接口 — 每种 ICommand 类型对应一个处理器
/// <para>遵循开闭原则：新增命令类型只需新增处理器并注册，无需修改 GameLoop。</para>
/// </summary>
/// <typeparam name="TCommand">处理的命令类型</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>
    /// 处理命令
    /// </summary>
    /// <param name="command">命令实例</param>
    /// <param name="ctx">处理上下文（提供所有引擎依赖）</param>
    void Handle(TCommand command, ICommandContext ctx);
}
