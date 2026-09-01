namespace LingFanEngine.Dsl.ProjectIndex;

/// <summary>资源索引：扫描项目根下全部资源文件（按扩展名分类），建内存表；查询 O(1)/前缀匹配。
/// <para>性能优先：全量扫描进内存，查询不碰磁盘；didChangeWatchedFiles 经 Update/Remove 增量维护。</para></summary>
public sealed class ResourceIndex
{
    // 资源相对路径键 OrdinalIgnoreCase（Windows 磁盘语义）：DSL 里 "images/bg.png" 与磁盘 Images/BG.png 须命中同一条目，
    // 否则精确查找 miss → 触发「未找到资源」假警告（EndsWith 兜底是 O(n) 且可能命中错误同名文件）。
    private Dictionary<string, ResourceEntry> _byRelative = new(StringComparer.OrdinalIgnoreCase);
    private string? _root;

    public void Scan(string rootPath)
    {
        _root = rootPath;
        // 后台构建：先在局部字典上完成全量扫描，末了一次性替换字段引用（引用赋值为原子操作），
        // 读请求要么看到旧表、要么看到新表，不会读到半构建状态——从而不阻塞 LSP 读请求。
        var byRelative = new Dictionary<string, ResourceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ProjectScanner.Enumerate(rootPath, "*"))
        {
            var ext = Path.GetExtension(f);
            if (!ResourceClassifier.IsResourceExtension(ext)) continue;
            AddOrUpdate(byRelative, f);
        }
        _byRelative = byRelative;
    }

    public void UpdateFile(string absolutePath)
    {
        var ext = Path.GetExtension(absolutePath);
        if (!ResourceClassifier.IsResourceExtension(ext)) return;
        AddOrUpdate(_byRelative, absolutePath);
    }

    public void RemoveFile(string absolutePath)
    {
        if (_root == null) return;
        var rel = ToRelative(_root, absolutePath);
        _byRelative.Remove(rel);
    }

    private void AddOrUpdate(Dictionary<string, ResourceEntry> dict, string absolutePath)
    {
        if (_root == null) return;
        try
        {
            var rel = ToRelative(_root, absolutePath);
            var kind = ResourceClassifier.Classify(Path.GetExtension(absolutePath)) ?? ResourceKind.Other;
            long size = 0;
            try { size = new FileInfo(absolutePath).Length; }
            catch { }
            dict[rel] = new ResourceEntry(absolutePath, rel, Path.GetExtension(absolutePath), kind, size);
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
