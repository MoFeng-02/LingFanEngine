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
/// <para>加载：读取存档并恢复场景 + 堆栈 + 状态。</para>
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
                System.Diagnostics.Debug.WriteLine("[SaveLoadHandler] 当前场景不允许存档（Menu/UI）");
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
            // 加载存档（同步等待以保持时序）
            var loaded = Task.Run(() => ctx.SaveService.LoadAsync(sl.SlotId)).GetAwaiter().GetResult();
            if (loaded != null)
                ctx.ApplySaveData(loaded);
        }
    }
}
