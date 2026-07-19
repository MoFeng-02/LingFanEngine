using System.Collections.Concurrent;

namespace LingFanEngine.Views;

/// <summary>
/// 线程安全的 LRU 图片缓存——限制 Bitmap 数量上限，超出时 Dispose 最久未使用的。
/// <para>解决 s_imageCache 无上限导致长时间运行 OOM 的问题。</para>
/// <para>AOT 友好：不使用反射，纯 ConcurrentDictionary + lock。</para>
/// </summary>
internal sealed class LruImageCache : IDisposable
{
    private readonly ConcurrentDictionary<string, Avalonia.Media.Imaging.Bitmap> _store = new();
    private readonly LinkedList<string> _order = new();
    private readonly object _lock = new();
    private readonly int _maxCapacity;
    private bool _disposed;

    /// <param name="maxCapacity">最大缓存条目数（超出时 Dispose 最旧的）</param>
    public LruImageCache(int maxCapacity = 128)
    {
        if (maxCapacity < 1) maxCapacity = 128;
        _maxCapacity = maxCapacity;
    }

    /// <summary>尝试获取缓存的 Bitmap，命中时更新访问顺序</summary>
    public Avalonia.Media.Imaging.Bitmap? TryGet(string key)
    {
        if (_store.TryGetValue(key, out var bmp))
        {
            // 更新访问顺序（移到链表头部）
            lock (_lock)
            {
                _order.Remove(key);
                _order.AddFirst(key);
            }
            return bmp;
        }
        return null;
    }

    /// <summary>添加 Bitmap 到缓存，超限时 Dispose 最旧条目</summary>
    public void Add(string key, Avalonia.Media.Imaging.Bitmap bmp)
    {
        _store[key] = bmp;
        lock (_lock)
        {
            _order.Remove(key);
            _order.AddFirst(key);

            // 淘汰超出的条目
            while (_order.Count > _maxCapacity)
            {
                var oldestKey = _order.Last!.Value;
                _order.RemoveLast();
                if (_store.TryRemove(oldestKey, out var oldBmp))
                {
                    try { oldBmp.Dispose(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LruImageCache] Dispose bitmap failed: {ex.Message}"); }
                }
            }
        }
    }

    /// <summary>当前缓存条目数</summary>
    public int Count => _store.Count;

    /// <summary>清空缓存并 Dispose 所有 Bitmap</summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var key in _order)
            {
                if (_store.TryRemove(key, out var bmp))
                {
                    try { bmp.Dispose(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LruImageCache] Dispose on clear failed: {ex.Message}"); }
                }
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
