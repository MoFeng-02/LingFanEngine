using System.Text.Json;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;
using LingFanEngine.Abstractions.Serialization;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core.Handlers;

/// <summary>
/// 存档/读档命令处理器
/// <para>保存：从当前状态构建存档数据并异步写入。</para>
/// <para>加载：异步读取存档并恢复场景 + 堆栈 + 状态。</para>
/// <para>P0-#5: 读档操作不再使用 sync-over-async（GetAwaiter().GetResult()），
/// 改为 fire-and-forget 异步加载，完成后在后台线程执行 ApplySaveData。</para>
/// </summary>
public class SaveLoadHandler : ICommandHandler<SaveLoadCommand>, IDefaultCommandHandler
{
    public void Handle(SaveLoadCommand sl, ICommandContext ctx)
    {
        if (ctx.SaveService == null) return;

        if (sl.IsSave)
        {
            var saveData = ctx.BuildSaveData();
            if (saveData == null)
            {
                System.Diagnostics.Debug.WriteLine("[SaveLoadHandler] 无正在进行的游戏，拒绝存档");
                return;
            }
            // 异步写入，失败时发用户可见通知（发布版 Debug.WriteLine 不可见，存档静默丢失不可接受）
            _ = ctx.SaveService.SaveAsync(sl.SlotId, saveData)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[SaveLoadHandler] Save failed: {t.Exception?.InnerException?.Message}");
                        NotifyError(ctx, $"存档失败：{t.Exception?.InnerException?.Message ?? "未知错误"}");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            // P0-#5: 异步加载——不再阻塞 GameLoop 线程
            // 加载完成后在后台线程执行 ApplySaveData（StateContainer 线程安全）
            _ = Task.Run(async () =>
            {
                try
                {
                    var loaded = await ctx.SaveService.LoadAsync(sl.SlotId);
                    if (loaded != null)
                    {
                        ctx.ApplySaveData(loaded);
                    }
                    else
                    {
                        // 读档失败：重启 DSL 从 load 命令的下一条继续（避免永久停摆）
                        System.Diagnostics.Debug.WriteLine($"[SaveLoadHandler] 读档失败: slot={sl.SlotId} 不存在或损坏");
                        ctx.DslExecutor?.Start();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveLoadHandler] 读档异常: {ex}");
                    ctx.ReportException(ex, nameof(SaveLoadHandler));
                    NotifyError(ctx, "读档失败：存档不存在或已损坏");
                    ctx.DslExecutor?.Start();
                }
            });
        }
    }

    /// <summary>
    /// 发送用户可见的错误通知（复用 Notify 命令的 OverlayRenderer 显示链路）。
    /// <para>直接写 StateKeys.Notify.Text 立即显示，不走队列——错误信息优先于排队中的普通通知。</para>
    /// </summary>
    private static void NotifyError(ICommandContext ctx, string message)
    {
        ctx.State.Set(StateKeys.Notify.Text, message);
        ctx.State.Set(StateKeys.Notify.Type, "error");
        ctx.State.Set(StateKeys.Notify.Duration, 4.0);
    }
}
