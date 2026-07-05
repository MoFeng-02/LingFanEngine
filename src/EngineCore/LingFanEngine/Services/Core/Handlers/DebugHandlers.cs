using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 调试日志命令处理器
/// <para>将调试消息写入状态容器的日志列表，供调试控制台读取。</para>
/// <para>仅当调试模式开启时才记录日志。</para>
/// </summary>
public class DebugLogHandler : ICommandHandler<DebugLogCommand>
{
    public void Handle(DebugLogCommand cmd, ICommandContext ctx)
    {
        // 未启用调试模式时不记录
        if (!ctx.State.Get<bool>(StateKeys.Debug.Enabled)) return;

        var logs = ctx.State.Get<List<DebugLogEntry>>(StateKeys.Debug.Logs) ?? [];
        var maxLogs = ctx.State.Get<int>(StateKeys.Debug.MaxLogs);
        if (maxLogs <= 0) maxLogs = 500;

        logs.Add(new DebugLogEntry
        {
            Level = cmd.Level,
            Message = cmd.Message,
            Source = ctx.State.Get<string>(StateKeys.Scene.CurrentName)
        });

        // 超出上限时移除最旧条目
        while (logs.Count > maxLogs)
            logs.RemoveAt(0);

        ctx.State.Set(StateKeys.Debug.Logs, logs);
    }
}
