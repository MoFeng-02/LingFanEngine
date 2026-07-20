namespace LingFanEngine.Views;

/// <summary>
/// 对话框模板注册表默认实现
/// <para>注册阶段用 Dictionary，运行时只读——AOT 友好，零反射。</para>
/// <para>线程安全：注册发生在启动期（单线程），运行时只读——无需锁。</para>
/// </summary>
public class DialogTemplateRegistry : IDialogTemplateRegistry
{
    private readonly Dictionary<string, IDialogBoxFactory> _templates = new();
    private string? _defaultName;

    public void Register(string name, IDialogBoxFactory factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _templates[name] = factory;  // 同名覆盖
    }

    public bool Unregister(string name) => _templates.Remove(name);

    public IDialogBoxFactory? Resolve(string? name) =>
        string.IsNullOrEmpty(name) ? null :
        _templates.TryGetValue(name, out var f) ? f : null;

    public IDialogBoxFactory? GetDefault() =>
        _defaultName != null && _templates.TryGetValue(_defaultName, out var f)
            ? f
            : (_templates.Count > 0 ? _templates.First().Value : null);

    public void SetDefault(string name)
    {
        if (!_templates.ContainsKey(name))
            throw new InvalidOperationException($"模板 '{name}' 未注册，无法设为默认");
        _defaultName = name;
    }

    public IReadOnlyCollection<string> RegisteredNames => _templates.Keys;
}
