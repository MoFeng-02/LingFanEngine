using System.Collections.Concurrent;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 状态容器实现
/// <para>所有运行时状态唯一存于此容器，底层为 ConcurrentDictionary。渲染层通过 GetSnapshot 获取只读快照。</para>
/// <para>支持点分路径访问嵌套字典：Get("player.hp") 会先查找扁平键 "player.hp"，
/// 若不存在则遍历 player → hp 嵌套字典。Set("player.hp", 100) 同理更新嵌套字典。</para>
/// <para>系统键（__ 前缀）使用下划线分隔，不受影响。</para>
/// </summary>
public class StateContainer : IStateContainer
{
    private readonly ConcurrentDictionary<string, object?> _store = new(StringComparer.Ordinal);
    private readonly IJsonValueConverter? _jsonConverter;

    /// <summary>
    /// 值变更事件——Set 写入成功后触发
    /// </summary>
    public event Action<string, object?>? ValueChanged;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="jsonConverter">JSON 值转换器（可选，用于归一化存档反序列化后的 JsonElement）</param>
    public StateContainer(IJsonValueConverter? jsonConverter = null)
    {
        _jsonConverter = jsonConverter;
    }

    /// <inheritdoc/>
    public void Set<T>(string key, T value)
    {
        // 支持点分路径：若 key 含 '.' 且父级字典已存在，则更新嵌套字典中的叶节点
        if (key.Contains('.') && TrySetPath(key, value))
        {
            ValueChanged?.Invoke(key, value);
            return;
        }
        _store[key] = value;
        ValueChanged?.Invoke(key, value);
    }

    /// <inheritdoc/>
    public T? Get<T>(string key)
    {
        // 1. 精确键匹配（优先）
        if (_store.TryGetValue(key, out var value))
        {
            value = Normalize(value);
            if (value is T typed)
                return typed;
            if (TryConvertValue<T>(value, out var converted))
                return converted;
        }

        // 2. 点分路径遍历（如 "player.hp" → store["player"]["hp"]）
        if (key.Contains('.'))
        {
            var found = Normalize(ResolvePath(key));
            if (found is T pathTyped)
                return pathTyped;
            if (TryConvertValue<T>(found, out var convertedPath))
                return convertedPath;
        }

        return default;
    }

    /// <inheritdoc/>
    public bool TryGet<T>(string key, out T? value)
    {
        // 1. 精确键匹配
        if (_store.TryGetValue(key, out var raw))
        {
            raw = Normalize(raw);
            if (raw is T typed)
            {
                value = typed;
                return true;
            }
            if (TryConvertValue<T>(raw, out var converted))
            {
                value = converted;
                return true;
            }
        }

        // 2. 点分路径遍历
        if (key.Contains('.'))
        {
            var found = Normalize(ResolvePath(key));
            if (found is T pathTyped)
            {
                value = pathTyped;
                return true;
            }
            if (TryConvertValue<T>(found, out var convertedPath))
            {
                value = convertedPath;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <inheritdoc/>
    public bool ContainsKey(string key)
    {
        if (_store.ContainsKey(key))
            return true;
        if (key.Contains('.'))
            return ResolvePath(key) != null;
        return false;
    }

    /// <inheritdoc/>
    public bool Remove(string key)
    {
        if (_store.TryRemove(key, out _))
            return true;
        if (key.Contains('.'))
            return TryRemovePath(key);
        return false;
    }

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _store.Keys;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> GetSnapshot()
    {
        // P0-#4: 使用 ToArray() 原子快照，避免 ConcurrentDictionary 弱一致性枚举器
        // 在遍历过程中遗漏并发写入的键
        var pairs = _store.ToArray();
        var dict = new Dictionary<string, object?>(pairs.Length, StringComparer.Ordinal);
        foreach (var (k, v) in pairs)
            dict[k] = v;
        return dict;
    }

    /// <inheritdoc/>
    public void Clear() => _store.Clear();

    // ========== 嵌套字典路径支持 ==========

    /// <summary>
    /// 前置归一化：将 JsonElement 转为 .NET 原生类型
    /// <para>存档反序列化后，嵌套字典内部的值可能仍是 JsonElement（未被 ConvertJsonValue 递归处理）。</para>
    /// <para>此方法确保 Get&lt;T&gt; 永远不会把 JsonElement 泄漏给调用方——包括 Get&lt;object&gt;。</para>
    /// <para>委托给 IJsonValueConverter.Convert，复用用户通过 RegisterCustomConverter 注册的自定义转换器。</para>
    /// </summary>
private object? Normalize(object? value)
{
    if (value is System.Text.Json.JsonElement && _jsonConverter != null)
        return _jsonConverter.Convert(value);
    return value;
}

    /// <summary>
    /// 尝试将已归一化的值安全转换为目标类型 T（AOT 安全，无反射）
    /// <para>处理数值 widening/narrowing：short→int, int→double, int→long, string→int 等。</para>
    /// <para>调用前值应已通过 Normalize 归一化（不含 JsonElement）。</para>
    /// </summary>
    private static bool TryConvertValue<T>(object? value, out T? result)
    {
        result = default;
        if (value == null) return false;

        // 枚举：从底层整数值还原为 T（AOT 安全——typeof(T) 为编译期已知，Enum.ToObject/GetUnderlyingType 均为 BCL 非反射 API）
        if (typeof(T).IsEnum)
        {
            try
            {
                if (value is IConvertible conv)
                {
                    var underlying = System.Enum.GetUnderlyingType(typeof(T));
                    result = (T)System.Enum.ToObject(typeof(T),
                        System.Convert.ChangeType(conv, underlying, System.Globalization.CultureInfo.InvariantCulture));
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StateContainer] Enum 转换失败: {ex.Message}");
            }
            return false;
        }

        // IConvertible → T（覆盖 short→int, int→double, int→long, double→int 等所有基元互转）
        // string→int 也由 IConvertible 处理（string 实现 IConvertible）
        if (value is IConvertible && typeof(T).IsPrimitive)
        {
            try
            {
                result = (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StateContainer] ChangeType 失败: {ex.Message}");
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// 按点分路径解析嵌套字典中的值
    /// <para>如 "player.stats.hp" → store["player"]["stats"]["hp"]</para>
    /// <para>支持 Dictionary&lt;string, object?&gt;（运行时等价于 Dictionary&lt;string, object&gt;）和 IDictionary</para>
    /// </summary>
    private object? ResolvePath(string key)
    {
        var parts = key.Split('.');
        if (parts.Length < 2) return null;

        if (!_store.TryGetValue(parts[0], out var current))
            return null;

        for (int i = 1; i < parts.Length; i++)
        {
            if (current is Dictionary<string, object?> dict)
            {
                if (!dict.TryGetValue(parts[i], out current))
                    return null;
            }
            else if (current is System.Collections.IDictionary idict)
            {
                if (!idict.Contains(parts[i]))
                    return null;
                current = idict[parts[i]];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>
    /// 按点分路径设置嵌套字典中的值
    /// <para>如 Set("player.hp", 100) → store["player"]["hp"] = 100</para>
    /// <para>仅当父级字典链已存在时才更新叶节点，否则返回 false 由调用方回退到扁平键。</para>
    /// </summary>
    private bool TrySetPath<T>(string key, T value)
    {
        var parts = key.Split('.');
        if (parts.Length < 2) return false;

        if (!_store.TryGetValue(parts[0], out var current))
            return false;

        // 遍历到倒数第二层（叶节点的父级）
        for (int i = 1; i < parts.Length - 1; i++)
        {
            if (current is Dictionary<string, object?> dict)
            {
                if (!dict.TryGetValue(parts[i], out current))
                    return false;
            }
            else if (current is System.Collections.IDictionary idict)
            {
                current = idict.Contains(parts[i]) ? idict[parts[i]] : null;
                if (current == null) return false;
            }
            else
            {
                return false;
            }
        }

        // 设置叶节点
        var leafKey = parts[^1];
        if (current is Dictionary<string, object?> leafDict)
        {
            leafDict[leafKey] = value;
            return true;
        }
        if (current is System.Collections.IDictionary ileafDict)
        {
            ileafDict[leafKey] = value;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 按点分路径删除嵌套字典中的键
    /// </summary>
    private bool TryRemovePath(string key)
    {
        var parts = key.Split('.');
        if (parts.Length < 2) return false;

        if (!_store.TryGetValue(parts[0], out var current))
            return false;

        // 遍历到倒数第二层
        for (int i = 1; i < parts.Length - 1; i++)
        {
            if (current is Dictionary<string, object?> dict)
            {
                if (!dict.TryGetValue(parts[i], out current))
                    return false;
            }
            else if (current is System.Collections.IDictionary idict)
            {
                current = idict.Contains(parts[i]) ? idict[parts[i]] : null;
                if (current == null) return false;
            }
            else
            {
                return false;
            }
        }

        var leafKey = parts[^1];
        if (current is Dictionary<string, object?> leafDict)
            return leafDict.Remove(leafKey);
        if (current is System.Collections.IDictionary ileafDict)
        {
            if (!ileafDict.Contains(leafKey)) return false;
            ileafDict.Remove(leafKey);
            return true;
        }
        return false;
    }
}
