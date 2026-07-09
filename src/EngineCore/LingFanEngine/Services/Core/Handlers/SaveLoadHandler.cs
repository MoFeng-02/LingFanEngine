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
            // 异步写入，记录异常防止静默丢失
            _ = ctx.SaveService.SaveAsync(sl.SlotId, saveData)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        System.Diagnostics.Debug.WriteLine(
                            $"[SaveLoadHandler] Save failed: {t.Exception?.InnerException?.Message}");
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
                    ctx.DslExecutor?.Start();
                }
            });
        }
    }
}
