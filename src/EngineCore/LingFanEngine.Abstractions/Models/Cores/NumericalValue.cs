using LingFanEngine.Abstractions.Models.Enums;

namespace LingFanEngine.Abstractions.Models.Cores;

/// <summary>
/// 数值，吼吼，这个就是，字典咯！
/// <para>关系提供？抱歉那是逻辑层提供的事，和我引擎核心无关</para>
/// </summary>
public class NumericalValue
{
    /// <summary>
    /// 吼吼吼，这个ID是面向不知道的，好吧可以是玩家也可以是NPC！
    /// </summary>
    public Guid CoreId { get; set; }

    /// <summary>
    /// 角色种类（Player、NPC）
    /// </summary>
    public CharacterKind CharacterKind { get; set; }

    /// <summary>
    /// wat？为什么是string绑object？哦！原来是这样的
    /// <para>string是相当于可观测key，而object你可以是任意值，没错是这样了！但是！绝对不允许为空！</para>
    /// </summary>
    public required Dictionary<string, object> Values { get; set; }
}

