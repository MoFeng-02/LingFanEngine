using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Entry;

namespace LingFanEngine.Services.Entry;

/// <summary>
/// 命令服务实现（精简版）
/// <para>职责分离：命令经管道投递到主循环；事件使用 EventAggregator 注册/发布。</para>
/// <para>字符串命令兼容层保留，用于 BaseEntity.Command 字段的消费。</para>
/// </summary>
public class CommandService : ICommandService
{
    private readonly ICommandPipeline _pipeline;
    private readonly EventAggregator _eventAggregator = new();

    /// <summary>字符串命令处理器（兼容 BaseEntity.Command 字段）</summary>
    private readonly Dictionary<string, List<Func<object?, CancellationToken, Task>>> _stringCommandHandlers = new();

    /// <summary>
    /// 构造函数
    /// </summary>
    public CommandService(ICommandPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc/>
    public ValueTask SendCommandAsync(ICommand command, CancellationToken ct = default)
    {
        return _pipeline.SendAsync(command, ct);
    }

    /// <inheritdoc/>
    public void RegisterCommand(string commandName, Func<object?, CancellationToken, Task> handler)
    {
        if (!_stringCommandHandlers.TryGetValue(commandName, out var handlers))
        {
            handlers = [];
            _stringCommandHandlers[commandName] = handlers;
        }
        handlers.Add(handler);
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(string commandName, object? commandValue, CancellationToken ct = default)
    {
        if (_stringCommandHandlers.TryGetValue(commandName, out var handlers))
        {
            var tasks = handlers.ToList().Select(h => h(commandValue, ct)).ToList();
            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
        }
        // 未找到时回退到 __dsl_fallback（把命令名和命令值都传入）
        else if (_stringCommandHandlers.TryGetValue(StateKeys.DslFallback, out var fallback))
        {
            foreach (var h in fallback)
                await h((commandName, commandValue ?? ""), ct);
        }
    }

    /// <inheritdoc/>
    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        return _eventAggregator.Subscribe(handler);
    }

    /// <inheritdoc/>
    public void Publish<TEvent>(TEvent evt) where TEvent : class
    {
        _eventAggregator.Publish(evt);
    }

    /// <inheritdoc/>
    public Task PublishAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : class
    {
        return _eventAggregator.PublishAsync(evt, ct);
    }
}
