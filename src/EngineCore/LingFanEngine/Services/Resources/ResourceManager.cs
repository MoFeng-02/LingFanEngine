using System.Collections.Concurrent;

namespace LingFanEngine.Services.Resources;

/// <summary>
/// 资源缓存条目
/// </summary>
internal class CacheEntry
{
    public required string Path { get; init; }
    public required object? Data { get; set; }
    public long Size { get; set; }
    public DateTime LastAccess { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 资源管理器
/// <para>统一管理游戏资源（图片、音频、配置等）的异步加载与 LRU 缓存。</para>
/// <para>使用 System.IO.Pipelines 实现零拷贝流加载。</para>
/// </summary>
public class ResourceManager : IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _basePath;
    private readonly int _maxCacheSize; // 最大缓存条目数
    private readonly long _maxCacheBytes; // 最大缓存字节数
    private long _currentCacheBytes;
    private PackLoader? _packLoader; // 加密资源包加载器
    private readonly SemaphoreSlim _loadSemaphore = new(4, 4); // 并发加载限制
    private bool _disposed;

    /// <summary>
    /// 缓存统计
    /// </summary>
    public record CacheStats(int EntryCount, long CurrentBytes, long MaxBytes);

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="basePath">资源根目录</param>
    /// <param name="maxCacheEntries">最大缓存条目（默认 256）</param>
    /// <param name="maxCacheMB">最大缓存 MB（默认 512）</param>
    public ResourceManager(string basePath = "Assets", int maxCacheEntries = 256, int maxCacheMB = 512)
    {
        _basePath = basePath;
        _maxCacheSize = maxCacheEntries;
        _maxCacheBytes = maxCacheMB * 1024L * 1024L;
    }

    /// <summary>
    /// 挂载加密资源包
    /// <para>通过 PackLoader 加载 AES 加密的 ZIP 包到内存缓存中。</para>
    /// </summary>
    /// <param name="packPath">加密包文件路径</param>
    /// <param name="key">AES 密钥（32 字节）</param>
    /// <param name="iv">AES 初始向量（16 字节）</param>
    /// <summary>
    /// 异步挂载加密资源包
    /// </summary>
    public async ValueTask MountPackAsync(string packPath, byte[] key, byte[]? iv = null,
        CancellationToken ct = default)
    {
        var loader = new PackLoader();
        await loader.MountAsync(packPath, key, iv, ct);
        _packLoader = loader;
    }

    /// <summary>
    /// 同步挂载加密资源包（兼容旧接口，内部异步执行）
    /// </summary>
    [Obsolete("请使用 MountPackAsync 异步版本")]
    public void MountPack(string packPath, byte[] key, byte[] iv)
        => MountPackAsync(packPath, key, iv).AsTask().GetAwaiter().GetResult();

    /// <summary>
    /// 获取缓存统计
    /// </summary>
    public CacheStats GetStats() => new(_cache.Count, _currentCacheBytes, _maxCacheBytes);

    /// <summary>
    /// 获取完整路径
    /// </summary>
    public string GetFullPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;
        return Path.Combine(_basePath, relativePath);
    }

    /// <summary>
    /// 异步加载字节数据（核心入口）
    /// </summary>
    /// <param name="relativePath">相对路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件字节数据，失败返回 null</returns>
    public async ValueTask<byte[]?> LoadBytesAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(relativePath);

        // 检查缓存
        if (_cache.TryGetValue(fullPath, out var entry) && entry.Data is byte[] data)
        {
            entry.LastAccess = DateTime.UtcNow;
            return data;
        }

        // 优先从加密资源包中读取
        if (_packLoader is not null)
        {
            var packData = await _packLoader.ReadBytesAsync(relativePath, cancellationToken);
            if (packData is not null)
            {
                // 存入缓存以便下次快速访问
                var packEntry = new CacheEntry
                {
                    Path = fullPath,
                    Data = packData,
                    Size = packData.Length,
                    LastAccess = DateTime.UtcNow
                };
                _cache[fullPath] = packEntry;
                Interlocked.Add(ref _currentCacheBytes, packData.Length);
                return packData;
            }
        }

        if (!File.Exists(fullPath))
            return null;

        await _loadSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 双重检查缓存
            if (_cache.TryGetValue(fullPath, out var cachedEntry) && cachedEntry.Data is byte[] existingData)
            {
                cachedEntry.LastAccess = DateTime.UtcNow;
                return existingData;
            }

            // 读取文件到缓冲区
            var fileInfo = new FileInfo(fullPath);
            var buffer = GC.AllocateArray<byte>((int)fileInfo.Length, pinned: false);

            await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);

            // 从流中读取
            var offset = 0;
            var remaining = buffer.Length;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, remaining), cancellationToken);
                if (read == 0) break;
                offset += read;
                remaining -= read;
            }

            // 存入缓存
            var newEntry = new CacheEntry
            {
                Path = fullPath,
                Data = buffer,
                Size = buffer.Length,
                LastAccess = DateTime.UtcNow
            };

            _cache[fullPath] = newEntry;
            Interlocked.Add(ref _currentCacheBytes, buffer.Length);

            // 触发性淘汰
            EvictIfNeeded();

            return buffer;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ResourceManager] LoadBytesAsync failed: {relativePath} — {ex.Message}");
            return null;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }

    /// <summary>
    /// 异步加载文本文件（如 JSON 配置）
    /// </summary>
    public async ValueTask<string?> LoadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var data = await LoadBytesAsync(relativePath, cancellationToken);
        return data != null ? System.Text.Encoding.UTF8.GetString(data) : null;
    }

    /// <summary>
    /// 检查资源是否存在
    /// </summary>
    public bool Exists(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        return _cache.ContainsKey(fullPath)
            || (_packLoader is not null && _packLoader.Exists(relativePath))
            || File.Exists(fullPath);
    }

    /// <summary>
    /// 从缓存中移除指定资源
    /// </summary>
    public bool Evict(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (_cache.TryRemove(fullPath, out var entry))
        {
            Interlocked.Add(ref _currentCacheBytes, -entry.Size);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 清空缓存
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        Interlocked.Exchange(ref _currentCacheBytes, 0);
    }

    /// <summary>
    /// 淘汰最久未访问的缓存条目
    /// </summary>
    private void EvictIfNeeded()
    {
        while (_cache.Count > _maxCacheSize || _currentCacheBytes > _maxCacheBytes)
        {
            // 线性查找最久未访问——避免 OrderBy 全量排序
            CacheEntry? oldest = null;
            var oldestTime = DateTime.MaxValue;
            foreach (var entry in _cache.Values)
            {
                if (entry.LastAccess < oldestTime)
                {
                    oldestTime = entry.LastAccess;
                    oldest = entry;
                }
            }

            if (oldest == null) break;

            if (_cache.TryRemove(oldest.Path, out _))
            {
                Interlocked.Add(ref _currentCacheBytes, -oldest.Size);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearCache();
        _loadSemaphore.Dispose();
    }
}
