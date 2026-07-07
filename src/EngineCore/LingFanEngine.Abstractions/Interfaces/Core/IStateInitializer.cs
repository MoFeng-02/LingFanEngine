namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 状态初始化器接口
/// <para>负责在引擎启动时初始化所有默认状态键值。</para>
/// </summary>
public interface IStateInitializer
{
    /// <summary>
    /// 初始化默认状态（仅当键不存在时写入）
    /// </summary>
    void Initialize(IStateContainer state);
}
