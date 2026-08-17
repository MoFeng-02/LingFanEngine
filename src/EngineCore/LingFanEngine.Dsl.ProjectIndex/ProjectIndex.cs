namespace LingFanEngine.Dsl.ProjectIndex;

/// <inheritdoc />
public sealed class ProjectIndex : IProjectIndex
{
    private readonly ResourceIndex _resources = new();
    private readonly CsSymbolIndex _csSymbols = new();

    public void Scan(string rootPath)
    {
        _resources.Scan(rootPath);
        _csSymbols.Scan(rootPath); // M8：C# 符号联动
    }

    public void UpdateResource(string absolutePath) => _resources.UpdateFile(absolutePath);
    public void RemoveResource(string absolutePath) => _resources.RemoveFile(absolutePath);

    public IReadOnlyList<ResourceEntry> GetResourceCandidates(string prefix) => _resources.GetCandidates(prefix);
    public ResourceEntry? FindResource(string path) => _resources.Find(path);

    // M8 跨语言联动
    public IReadOnlyCollection<string> GetCommandNames() => _csSymbols.CommandNames;
    public IReadOnlyCollection<string> GetCsVariableKeys() => _csSymbols.VariableKeys;
    public IReadOnlyCollection<string> GetSceneTargets() => _csSymbols.SceneTargets;
    public IReadOnlyCollection<CsRef> GetCsResourceReferences(string relativePath) => _csSymbols.GetResourceReferences(relativePath);
}
