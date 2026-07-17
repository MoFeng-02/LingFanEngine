using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Models.Saves;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 存档数据服务接口
/// <para>负责存档数据的构建、恢复和系统偏好持久化。</para>
/// </summary>
public interface ISaveDataService
{
    /// <summary>
    /// 从当前状态构建存档数据
    /// <para>返回 null 表示当前场景不允许存档（如 Menu/UI 场景）。</para>
    /// </summary>
    SaveData? BuildSaveData();

    /// <summary>
    /// 应用存档数据到状态容器（恢复场景 + 堆栈 + 状态）
    /// <para>等同于 ApplySaveData(data, continueGame: true)——继续游戏模式。</para>
    /// </summary>
    void ApplySaveData(SaveData data);

    /// <summary>
    /// 应用存档数据到状态容器（恢复场景 + 堆栈 + 状态）
    /// </summary>
    /// <param name="data">存档数据</param>
    /// <param name="continueGame">
    /// true = 继续游戏（精确恢复 DslCurrentIndex，叙事模式默认）；
    /// false = 锚点读取（DslCurrentIndex 重置为 0，场景从头执行，小说世界模式专用）。
    /// </param>
    void ApplySaveData(SaveData data, bool continueGame);

    /// <summary>
    /// 保存系统偏好（所有非瞬态 __* 变量）到独立文件
    /// </summary>
    void SaveSystemState();

    /// <summary>
    /// 加载系统偏好（引擎初始化时调用一次）
    /// </summary>
    Task LoadSystemStateAsync();
}
