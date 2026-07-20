using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.DslCore;

/// <summary>
/// DSL 缓动函数名称集合——从 <see cref="EasingType"/> 枚举自动生成。
/// <para>SDK 补全器和高亮器统一引用此集合，确保与引擎核心同步。</para>
/// <para>DSL 中缓动函数值与 EasingType 枚举名完全一致（引擎使用 Enum.TryParse 解析）。</para>
/// </summary>
/// <remarks>
/// AOT 兼容：<c>Enum.GetNames&lt;T&gt;()</c> 对具体类型参数在 .NET 8+ AOT 下通过源生成器
/// 静态解析，无反射开销。引擎新增 / 删除缓动类型时，此集合自动同步。
/// </remarks>
public static class DslEasingNames
{
    /// <summary>
    /// 所有有效的缓动函数名称（与 <see cref="EasingType"/> 枚举名一致）。
    /// <para>使用默认序号比较器（区分大小写），与引擎 Enum.TryParse 行为一致。</para>
    /// </summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        Enum.GetNames<EasingType>());
}
