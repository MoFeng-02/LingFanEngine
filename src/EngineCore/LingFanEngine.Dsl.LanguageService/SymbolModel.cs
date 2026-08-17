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

    /// <summary>样式（style "name" ...；元素 class=/style= 引用）。</summary>
    Style,
}

/// <summary>符号出现角色——是定义还是引用。</summary>
public enum SymbolRole
{
    /// <summary>定义站点。</summary>
    Definition = 0,

    /// <summary>引用站点。</summary>
    Reference,
}

/// <summary>变量符号的生命周期级别（用于诊断语义，不影响跨文件解析）。
/// <list type="bullet">
///   <item><description><see cref="Global"/>：<c>define</c> / <c>set</c> 注入，进程级全局可见，<c>define</c> 尤甚——无论写在哪个 scene / 文件都恒为全局。</description></item>
///   <item><description><see cref="Scene"/>：<c>label</c> 等绑定到所属 scene 的局部命名（jump 默认指同 scene 内的 label）。</description></item>
///   <item><description><see cref="Local"/>：<c>let</c> / <c>local</c> 声明的块级/场景级局部变量。</description></item>
/// </list>
/// </summary>
public enum SymbolScope
{
    /// <summary>全局（define / set）。</summary>
    Global = 0,
    /// <summary>场景级（label 等）。</summary>
    Scene,
    /// <summary>局部（let / local）。</summary>
    Local,
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
    public SymbolScope Scope { get; }
    /// <summary>是否为「声明式」定义（define / scene / character / label / func）。
    /// <c>set</c> 是赋值、<c>let</c>/<c>local</c> 是块级声明，二者均不计入「重复定义」告警；
    /// 只有本字段为 true 的符号才参与重复定义检测。</summary>
    public bool IsDeclaration { get; }
    public string Name { get; }
    public string FilePath { get; }
    /// <summary>符号名在源中的起始偏移（字符索引）。</summary>
    public int Offset { get; }
    /// <summary>符号名的字符长度。</summary>
    public int Length { get; }

    /// <summary>默认构造：生命周期级别按 <see cref="SymbolScope.Global"/>（场景/函数/角色等大部分定义均为全局），非声明式。</summary>
    public SymbolOccurrence(SymbolKind kind, SymbolRole role, string name, string filePath, int offset, int length)
        : this(kind, role, name, filePath, offset, length, SymbolScope.Global, false) { }

    /// <summary>显式指定生命周期级别（label→Scene、let/local→Local），非声明式。</summary>
    public SymbolOccurrence(SymbolKind kind, SymbolRole role, string name, string filePath, int offset, int length, SymbolScope scope)
        : this(kind, role, name, filePath, offset, length, scope, false) { }

    /// <summary>显式指定生命周期级别与是否声明式定义。</summary>
    public SymbolOccurrence(SymbolKind kind, SymbolRole role, string name, string filePath, int offset, int length, SymbolScope scope, bool isDeclaration)
    {
        Kind = kind;
        Role = role;
        Scope = scope;
        IsDeclaration = isDeclaration;
        Name = name;
        FilePath = filePath;
        Offset = offset;
        Length = length;
    }

    public SymbolKey Key => new(Kind, Name);
}
