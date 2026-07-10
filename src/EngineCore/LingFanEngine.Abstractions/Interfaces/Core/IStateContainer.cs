namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 状态容器接口
/// <para>所有运行时状态唯一存于此容器，SSOT（唯一真相源）。</para>
/// <para>Key 为 string（便于人类可读），V2 优化目标为 Source Generator 生成 int 句柄。</para>
/// </summary>
public interface IStateContainer
{
    /// <summary>
    /// 设置值
    /// </summary>
    void Set<T>(string key, T value);

    /// <summary>
    /// 获取值，不存在返回 default(T)
    /// </summary>
    T? Get<T>(string key);

    /// <summary>
    /// 尝试获取值
    /// </summary>
    bool TryGet<T>(string key, out T? value);

    /// <summary>
    /// 检查键是否存在
    /// </summary>
    bool ContainsKey(string key);

    /// <summary>
    /// 删除键
    /// </summary>
    bool Remove(string key);

    /// <summary>
    /// 获取当前所有键
    /// </summary>
    IEnumerable<string> Keys { get; }

    /// <summary>
    /// 获取只读快照（渲染层并行读取）
    /// </summary>
    IReadOnlyDictionary<string, object?> GetSnapshot();

    /// <summary>
    /// 清空所有状态
    /// </summary>
    void Clear();

    /// <summary>
    /// 值变更事件——Set 被调用且值写入成功后触发。
    /// <para>参数：(key, newValue)。</para>
    /// <para>轻量级：无订阅者时为 null，调用方做 null 检查后直接 invoke。</para>
    /// </summary>
    event Action<string, object?>? ValueChanged;
}
