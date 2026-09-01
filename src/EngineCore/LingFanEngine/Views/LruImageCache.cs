using System.Collections.Concurrent;

namespace LingFanEngine.Views;

/// <summary>
/// 线程安全的 LRU 图片缓存——限制 Bitmap 数量上限，超出时回收最久未使用的。
/// <para>解决 s_imageCache 无上限导致长时间运行 OOM 的问题。</para>
/// <para>2026-09 修复（引用计数）：Bitmap 会被多个 Image 控件共享，直接在淘汰时 Dispose
/// 会使仍显示中的控件渲染异常（图片消失/花屏）。现在淘汰仅标记，最后一个引用
/// （控件 DetachFromVisualTree）释放时才真正 Dispose。</para>
/// <para>AOT 友好：不使用反射，纯 ConcurrentDictionary + lock。</para>
/// </summary>
internal sealed class LruImageCache : IDisposable
{
    /// <summary>缓存条目：Bitmap + 活引用计数 + 淘汰标记</summary>
    private sealed class Entry
    {
        public required Avalonia.Media.Imaging.Bitmap Bmp;
        /// <summary>正在使用此 Bitmap 的 Image 控件数（Acquire/Release 配对）</summary>
        public int Refs;
        /// <summary>已从 LRU 淘汰（不在 _order 中），但可能仍被引用中——归零时 Dispose</summary>
        public bool Evicted;
    }

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly LinkedList<string> _order = new();
    private readonly object _lock = new();
    private readonly int _maxCapacity;
    private bool _disposed;

    /// <param name="maxCapacity">最大缓存条目数（超出时回收最旧的）</param>
    public LruImageCache(int maxCapacity = 128)
    {
        if (maxCapacity < 1) maxCapacity = 128;
        _maxCapacity = maxCapacity;
    }

    /// <summary>尝试获取缓存的 Bitmap，命中时更新访问顺序（不增引用——配对 Acquire）</summary>
    public Avalonia.Media.Imaging.Bitmap? TryGet(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return null;
        lock (_lock)
        {
            // 已淘汰条目不回挂 LRU（保持淘汰语义）；未淘汰的更新访问顺序
            if (!entry.Evicted)
            {
                _order.Remove(key);
                _order.AddFirst(key);
            }
        }
        return entry.Bmp;
    }

    /// <summary>添加 Bitmap 到缓存，超限时回收最旧条目（仍被引用的延迟到最后释放）</summary>
    public void Add(string key, Avalonia.Media.Imaging.Bitmap bmp)
    {
        _store[key] = new Entry { Bmp = bmp };
        lock (_lock)
        {
            _order.Remove(key);
            _order.AddFirst(key);

            // 淘汰超出的条目——仍被引用（Refs>0）的仅标记，延迟到 Release 归零时 Dispose
            while (_order.Count > _maxCapacity)
            {
                var oldestKey = _order.Last!.Value;
                _order.RemoveLast();
                if (_store.TryGetValue(oldestKey, out var oldest))
                    Evict(oldestKey, oldest);
            }
        }
    }

    /// <summary>登记一个使用引用（控件把缓存 Bitmap 设为 Source 时调用）</summary>
    public void Acquire(string key)
    {
        if (_store.TryGetValue(key, out var entry))
            Interlocked.Increment(ref entry.Refs);
    }

    /// <summary>释放一个使用引用（控件脱离视觉树时调用）——归零且已淘汰则 Dispose</summary>
    public void Release(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return;
        if (Interlocked.Decrement(ref entry.Refs) > 0 || !entry.Evicted)
            return;
        // 引用归零且已被淘汰——安全销毁（lock 防与 Add 的淘汰路径双重 Dispose）
        lock (_lock)
        {
            if (entry.Evicted && Volatile.Read(ref entry.Refs) <= 0)
                Evict(key, entry);
        }
    }

    /// <summary>标记淘汰并按需销毁（须在 _lock 内调用）</summary>
    private void Evict(string key, Entry entry)
    {
        entry.Evicted = true;
        if (Volatile.Read(ref entry.Refs) <= 0 && _store.TryRemove(key, out var removed))
        {
            try { removed.Bmp.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LruImageCache] Dispose bitmap failed: {ex.Message}"); }
        }
    }

    /// <summary>当前缓存条目数</summary>
    public int Count => _store.Count;

    /// <summary>清空缓存；仍被引用的条目延迟到最后释放时 Dispose</summary>
    public void Clear()
    {
        lock (_lock)
        {
            // 遍历 _store（非 _order——已淘汰未销毁的条目不在 _order 中）
            foreach (var key in _store.Keys)
            {
                if (_store.TryGetValue(key, out var entry))
                    Evict(key, entry);
            }
            _order.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}
