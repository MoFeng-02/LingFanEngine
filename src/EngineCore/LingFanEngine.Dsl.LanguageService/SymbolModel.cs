namespace LingFanEngine.Dsl.LanguageService;

/// <summary>符号种类——定义站点与引用站点共有的分类键。</summary>
public enum SymbolKind
{
    /// <summary>场景（scene "name"）。</summary>
    Scene = 0,

    /// <summary>标签（label name:）。</summary>
    Label,

    /// <summary>变量（set/define/let/local name = ...）。</summary>
    Variable,

    /// <summary>角色（character "key"）。</summary>
    Character,

    /// <summary>函数（func name ... / call name）。</summary>
    Func,
}

/// <summary>符号出现角色——是定义还是引用。</summary>
public enum SymbolRole
{
    /// <summary>定义站点。</summary>
    Definition = 0,

    /// <summary>引用站点。</summary>
    Reference,
}

/// <summary>
/// 符号键——(种类, 名称) 二元组，作为跨文件索引的字典键。
/// <para>实现 <see cref="IEquatable{T}"/> 与基于名称哈希的相等判定，AOT 友好。</para>
/// </summary>
public readonly struct SymbolKey : IEquatable<SymbolKey>
{
    public SymbolKind Kind { get; }
    public string Name { get; }

    public SymbolKey(SymbolKind kind, string name)
    {
        Kind = kind;
        Name = name;
    }

    public bool Equals(SymbolKey other) =>
        Kind == other.Kind && string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SymbolKey other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)Kind * 397) ^ (Name?.GetHashCode() ?? 0);
        }
    }
}

/// <summary>
/// 符号出现——一次定义或引用在源中的具体位置与名称。
/// <para>纯数据、默认不可变、NativeAOT 友好。</para>
/// </summary>
public readonly struct SymbolOccurrence
{
    public SymbolKind Kind { get; }
    public SymbolRole Role { get; }
    public string Name { get; }
    public string FilePath { get; }
    /// <summary>符号名在源中的起始偏移（字符索引）。</summary>
    public int Offset { get; }
    /// <summary>符号名的字符长度。</summary>
    public int Length { get; }

    public SymbolOccurrence(SymbolKind kind, SymbolRole role, string name, string filePath, int offset, int length)
    {
        Kind = kind;
        Role = role;
        Name = name;
        FilePath = filePath;
        Offset = offset;
        Length = length;
    }

    public SymbolKey Key => new(Kind, Name);
}
