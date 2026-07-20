using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 时间线回退命令处理器（鼠标滚轮上滚）
/// <para>在 DSL 检查点列表中向后移动一步，显示上一个交互点的完整状态。</para>
/// <para>跨场景：检查点包含全量状态快照，恢复时自动还原场景命令和元素。</para>
/// <para>检查点耗尽时直接返回——不回退到 SceneStack。</para>
/// </summary>
public class RollbackHandler : ICommandHandler<RollbackCommand>, IDefaultCommandHandler
{
    public void Handle(RollbackCommand rc, ICommandContext ctx)
    {
        if (ctx.DslExecutor != null && ctx.DslExecutor.CanRollback())
            ctx.DslExecutor.Rollback();
    }
}

/// <summary>
/// 时间线前进命令处理器（鼠标滚轮下滚）
/// <para>在 DSL 检查点列表中向前移动一步，恢复下一个交互点的完整状态。</para>
/// <para>只有回退过后才能前进——到达时间线前沿后此命令无效。</para>
/// </summary>
public class RollforwardHandler : ICommandHandler<RollforwardCommand>, IDefaultCommandHandler
{
    public void Handle(RollforwardCommand rc, ICommandContext ctx)
    {
        if (ctx.DslExecutor != null && ctx.DslExecutor.CanRollforward())
            ctx.DslExecutor.Rollforward();
    }
}

/// <summary>
/// 回溯到指定检查点命令处理器（从历史面板跳转）
/// </summary>
public class RollbackToHandler : ICommandHandler<RollbackToCommand>, IDefaultCommandHandler
{
    public void Handle(RollbackToCommand rtc, ICommandContext ctx)
    {
        if (ctx.DslExecutor == null) return;

        var targetPos = rtc.TargetCheckpointIndex;
        if (targetPos < 0)
        {
            // 等价于 BackAsync
            ctx.DslExecutor.Rollback();
            return;
        }

        ctx.DslExecutor.RollbackTo(targetPos);
    }
}
