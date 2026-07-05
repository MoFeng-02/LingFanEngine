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

    /// <inheritdoc/>
    public void Set<T>(string key, T value)
    {
        // 支持点分路径：若 key 含 '.' 且父级字典已存在，则更新嵌套字典中的叶节点
        if (key.Contains('.') && TrySetPath(key, value))
            return;
        _store[key] = value;
    }

    /// <inheritdoc/>
    public T? Get<T>(string key)
    {
        // 1. 精确键匹配（优先）
        if (_store.TryGetValue(key, out var value) && value is T typed)
            return typed;

        // 2. 点分路径遍历（如 "player.hp" → store["player"]["hp"]）
        if (key.Contains('.'))
        {
            var found = ResolvePath(key);
            if (found is T pathTyped)
                return pathTyped;
        }

        return default;
    }

    /// <inheritdoc/>
    public bool TryGet<T>(string key, out T? value)
    {
        // 1. 精确键匹配
        if (_store.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        // 2. 点分路径遍历
        if (key.Contains('.'))
        {
            var found = ResolvePath(key);
            if (found is T pathTyped)
            {
                value = pathTyped;
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
        // 创建快照拷贝，保证渲染层读到的是一致状态
        return new Dictionary<string, object?>(_store, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public void Clear() => _store.Clear();

    // ========== 嵌套字典路径支持 ==========

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
