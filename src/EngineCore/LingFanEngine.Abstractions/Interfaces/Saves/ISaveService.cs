using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;

namespace LingFanEngine.Abstractions.Interfaces.Saves;

/// <summary>
/// 存档服务接口
/// <para>定义存档的加载、保存、删除等操作</para>
/// </summary>
public interface ISaveService
{
    /// <summary>
    /// 加载存档
    /// </summary>
    /// <param name="slotId">存档槽标识</param>
    /// <returns>存档数据，不存在返回 null</returns>
    Task<SaveData?> LoadAsync(string slotId);

    /// <summary>
    /// 保存存档
    /// </summary>
    /// <param name="slotId">存档槽标识</param>
    /// <param name="data">存档数据</param>
    Task SaveAsync(string slotId, SaveData data);

    /// <summary>
    /// 删除存档
    /// </summary>
    /// <param name="slotId">存档槽标识</param>
    Task DeleteAsync(string slotId);

    /// <summary>
    /// 获取所有存档槽信息（不包含完整数据）
    /// </summary>
    Task<IEnumerable<SaveSlotInfo>> GetAllSaveSlotsAsync();

    /// <summary>
    /// 检查存档是否存在
    /// </summary>
    /// <param name="slotId">存档槽标识</param>
    Task<bool> ExistsAsync(string slotId);
}