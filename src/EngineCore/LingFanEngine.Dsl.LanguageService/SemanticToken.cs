using LingFanEngine.DslCore;

namespace LingFanEngine.Dsl.LanguageService;

/// <summary>
/// 语义高亮单元——供前端一次性渲染的 (offset, length, category) 三元组。
/// <para>纯数据、默认不可变、NativeAOT 友好（无反射 / 无动态代码生成）。</para>
/// </summary>
public readonly struct SemanticToken
{
    /// <summary>token 在源文本中的起始偏移（字符索引）。</summary>
    public int Offset { get; }

    /// <summary>token 的字符长度。</summary>
    public int Length { get; }

    /// <summary>语义高亮类别。</summary>
    public SemanticCategory Category { get; }

    /// <summary>构造一个语义 token。</summary>
    public SemanticToken(int offset, int length, SemanticCategory category)
    {
        Offset = offset;
        Length = length;
        Category = category;
    }

    /// <summary>从源文本切片取得 token 文本（返回源的子区间视图）。</summary>
    public ReadOnlySpan<char> GetText(ReadOnlySpan<char> source) => source.Slice(Offset, Length);
}
