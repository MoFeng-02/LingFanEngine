namespace LingFanEngine.Dsl.ProjectIndex;

/// <summary>项目联合索引门面（资源 + 未来的 C# 符号）。LSP 在 initialized 时 Scan(rootPath)，
/// 补全/悬停/跳转从它取项目级数据，与 DSL 符号索引（DslSymbolIndex）解耦、各自负责。</summary>
public interface IProjectIndex
{
    /// <summary>全量扫描项目根（资源 + 未来的 C# 符号）。</summary>
    void Scan(string rootPath);

    /// <summary>资源文件新增/变更（供 didChangeWatchedFiles 增量）。</summary>
    void UpdateResource(string absolutePath);

    /// <summary>资源文件删除（供 didChangeWatchedFiles 增量）。</summary>
    void RemoveResource(string absolutePath);

    /// <summary>按相对路径前缀取资源补全候选。</summary>
    IReadOnlyList<ResourceEntry> GetResourceCandidates(string prefix);

    /// <summary>精确/末尾匹配查找资源条目（用于悬停与跳转）。</summary>
    ResourceEntry? FindResource(string path);

    // M8 跨语言联动
    IReadOnlyCollection<string> GetCommandNames();
    IReadOnlyCollection<string> GetCsVariableKeys();
    /// <summary>C# 侧定义的场景导航目标（Navigate("x") 等），供 nav= 补全增强。</summary>
    IReadOnlyCollection<string> GetSceneTargets();
    /// <summary>某资源相对路径被哪些 C# 文件引用（资源对照查找）。</summary>
    IReadOnlyCollection<CsRef> GetCsResourceReferences(string relativePath);
}
