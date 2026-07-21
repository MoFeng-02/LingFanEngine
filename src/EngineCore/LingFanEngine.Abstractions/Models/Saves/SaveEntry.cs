using System.Text.Json.Serialization;

namespace LingFanEngine.Abstractions.Models.Saves;

/// <summary>
/// 带类型标识的存档条目
/// <para>解决 Dictionary&lt;string, object?&gt; 序列化后类型丢失（JsonElement）的问题。</para>
/// <para>每个值附带类型标识，反序列化时根据类型标识精确还原。</para>
/// </summary>
public class SaveEntry
{
    /// <summary>
    /// 类型标识（如 "int", "long", "double", "bool", "string", "list_ui", "dict_str_obj"）
    /// </summary>
    [JsonPropertyName("t")]
    public string Type { get; set; } = SaveEntryTypes.String;

    /// <summary>
    /// 值（序列化时按类型标识存储原始 JSON 值）
    /// </summary>
    [JsonPropertyName("v")]
    public object? Value { get; set; }
}

/// <summary>
/// 存档条目类型标识常量
/// </summary>
public static class SaveEntryTypes
{
    public const string Int = "int";
    public const string Long = "long";
    public const string Double = "double";
    public const string Float = "float";
    public const string Bool = "bool";
    public const string String = "string";
    public const string Null = "null";
    public const string ListUIElement = "list_ui";
    public const string DictStringObject = "dict_str_obj";
    public const string Decimal = "decimal";
    public const string DateTime = "datetime";
    public const string Guid = "guid";
    /// <summary>
    /// 任意可枚举对象 / 嵌套结构的 JSON 序列化存储（AOT 零反射，元素转 object? 后由 LfJsonContext.Default.ListObject 序列化）。
    /// 读档时解析为 JsonElement 并经 JsonValueConverter 还原为 List&lt;object?&gt; / Dictionary&lt;string,object?&gt;，避免静默丢失。
    /// </summary>
    public const string Json = "json";
}
