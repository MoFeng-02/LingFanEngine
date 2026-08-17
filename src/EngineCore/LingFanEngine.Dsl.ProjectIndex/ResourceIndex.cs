namespace LingFanEngine.Dsl.ProjectIndex;

/// <summary>资源索引：扫描项目根下全部资源文件（按扩展名分类），建内存表；查询 O(1)/前缀匹配。
/// <para>性能优先：全量扫描进内存，查询不碰磁盘；didChangeWatchedFiles 经 Update/Remove 增量维护。</para></summary>
public sealed class ResourceIndex
{
    private readonly Dictionary<string, ResourceEntry> _byRelative = new(StringComparer.Ordinal);
    private string? _root;

    public void Scan(string rootPath)
    {
        _root = rootPath;
        _byRelative.Clear();
        foreach (var f in ProjectScanner.Enumerate(rootPath, "*"))
        {
            var ext = Path.GetExtension(f);
            if (!ResourceClassifier.IsResourceExtension(ext)) continue;
            AddOrUpdate(f);
        }
    }

    public void UpdateFile(string absolutePath)
    {
        var ext = Path.GetExtension(absolutePath);
        if (!ResourceClassifier.IsResourceExtension(ext)) return;
        AddOrUpdate(absolutePath);
    }

    public void RemoveFile(string absolutePath)
    {
        if (_root == null) return;
        var rel = ToRelative(_root, absolutePath);
        _byRelative.Remove(rel);
    }

    private void AddOrUpdate(string absolutePath)
    {
        if (_root == null) return;
        try
        {
            var rel = ToRelative(_root, absolutePath);
            var kind = ResourceClassifier.Classify(Path.GetExtension(absolutePath)) ?? ResourceKind.Other;
            long size = 0;
            try { size = new FileInfo(absolutePath).Length; }
            catch { }
            _byRelative[rel] = new ResourceEntry(absolutePath, rel, Path.GetExtension(absolutePath), kind, size);
        }
        catch { }
    }

    /// <summary>按相对路径前缀模糊匹配（大小写不敏感），返回补全候选。</summary>
    public IReadOnlyList<ResourceEntry> GetCandidates(string prefix)
    {
        var p = prefix.Replace('\\', '/').TrimEnd('/');
        var list = new List<ResourceEntry>(_byRelative.Count);
        foreach (var kv in _byRelative)
            if (kv.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)) list.Add(kv.Value);
        return list;
    }

    /// <summary>精确或末尾匹配查找资源（用户输入可能省略目录前缀）。</summary>
    public ResourceEntry? Find(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var s = path.Replace('\\', '/').Trim();
        if (_byRelative.TryGetValue(s, out var exact)) return exact;
        foreach (var kv in _byRelative)
            if (kv.Key.EndsWith(s, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    public IReadOnlyCollection<ResourceEntry> All => _byRelative.Values;

    private static string ToRelative(string root, string absolute)
    {
        var r = root.Replace('\\', '/').TrimEnd('/');
        var a = absolute.Replace('\\', '/');
        if (a.Length > r.Length && a.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase))
            return a.Substring(r.Length + 1);
        return a;
    }
}
