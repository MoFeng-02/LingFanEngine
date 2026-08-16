namespace LingFanEngine.DslCore;

/// <summary>
/// DSL 词法 token——仅记录源文本中的偏移与长度 + 类型，文本按需从源切片取得（零分配友好）。
/// <para>设计为 <see langword="readonly struct"/>，可被数组 / ImmutableArray 容纳，NativeAOT 友好（无反射 / 无动态代码生成）。</para>
/// </summary>
public readonly struct DslToken
{
    /// <summary>token 在源文本中的起始偏移（字符索引）。</summary>
    public int Offset { get; }

    /// <summary>token 的字符长度。</summary>
    public int Length { get; }

    /// <summary>词法类型。</summary>
    public DslTokenKind Kind { get; }

    /// <summary>构造一个 token。</summary>
    public DslToken(int offset, int length, DslTokenKind kind)
    {
        Offset = offset;
        Length = length;
        Kind = kind;
    }

    /// <summary>从源文本切片取得 token 文本（不分配新字符串，返回源的子区间视图）。</summary>
    public ReadOnlySpan<char> GetText(ReadOnlySpan<char> source) => source.Slice(Offset, Length);
}
