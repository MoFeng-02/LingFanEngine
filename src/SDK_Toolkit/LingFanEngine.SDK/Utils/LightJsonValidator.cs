using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LingFanEngine.SDK.Utils;

/// <summary>
/// 轻量 JSON Schema（JSON Schema 子集）——用 C# 构建，供 AI 结构化输出/工具调用参数校验使用。
/// <para>覆盖：object/array/string/number/integer/boolean/null + properties/required/items/minItems/maxItems/minimum/maximum/enum。</para>
/// </summary>
public sealed class JsonSchema
{
    /// <summary>类型。取值：object|array|string|number|integer|boolean|null</summary>
    public string? Type { get; init; }

    /// <summary>对象属性表（仅 Type==object 时有效）</summary>
    public IReadOnlyDictionary<string, JsonSchema>? Properties { get; init; }

    /// <summary>对象必填属性名（仅 Type==object）</summary>
    public IReadOnlyList<string>? Required { get; init; }

    /// <summary>数组元素 schema（仅 Type==array）</summary>
    public JsonSchema? Items { get; init; }

    /// <summary>数组最小/最大长度（仅 Type==array）</summary>
    public int? MinItems { get; init; }

    /// <summary>数组最大长度（仅 Type==array）</summary>
    public int? MaxItems { get; init; }

    /// <summary>number/integer 的最小值</summary>
    public decimal? Minimum { get; init; }

    /// <summary>number/integer 的最大值</summary>
    public decimal? Maximum { get; init; }

    /// <summary>枚举允许值（string/number/boolean 基础标量）</summary>
    public IReadOnlyList<object>? Enum { get; init; }

    /// <summary>便捷：object schema 工厂（properties + required）</summary>
    public static JsonSchema Object(IReadOnlyDictionary<string, JsonSchema>? properties = null, IReadOnlyList<string>? required = null)
        => new() { Type = "object", Properties = properties, Required = required };

    /// <summary>便捷：array schema 工厂</summary>
    public static JsonSchema Array(JsonSchema? items = null, int? minItems = null, int? maxItems = null)
        => new() { Type = "array", Items = items, MinItems = minItems, MaxItems = maxItems };

    public static JsonSchema String(IReadOnlyList<object>? @enum = null) => new() { Type = "string", Enum = @enum };
    public static JsonSchema Number(decimal? minimum = null, decimal? maximum = null) => new() { Type = "number", Minimum = minimum, Maximum = maximum };
    public static JsonSchema Integer(decimal? minimum = null, decimal? maximum = null) => new() { Type = "integer", Minimum = minimum, Maximum = maximum };
    public static JsonSchema Boolean => new() { Type = "boolean" };
}

/// <summary>
/// JSON 校验器——把模型产出的 JSON（文本或 <see cref="JsonElement"/>）按 <see cref="JsonSchema"/> 校验。
/// <para>纯 <see cref="System.Text.Json"/> 遍历，零反射，AOT 安全；返回错误列表（空 = 合法）。</para>
/// </summary>
public static class LightJsonValidator
{
    /// <summary>校验一段 JSON 文本；返回空列表 = 合法。</summary>
    public static IReadOnlyList<string> Validate(string jsonText, JsonSchema schema)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            errors.Add("JSON 为空");
            return errors;
        }
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            ValidateElement(doc.RootElement, schema, "$", errors);
        }
        catch (JsonException ex)
        {
            errors.Add($"JSON 解析失败：{ex.Message}");
        }
        return errors;
    }

    /// <summary>校验一个已解析的元素；返回空列表 = 合法。</summary>
    public static IReadOnlyList<string> ValidateElement(JsonElement element, JsonSchema schema)
    {
        var errors = new List<string>();
        ValidateElement(element, schema, "$", errors);
        return errors;
    }

    private static void ValidateElement(JsonElement el, JsonSchema? schema, string path, List<string> errors)
    {
        if (schema == null)
            return;

        // 枚举（对任意标量型检查）
        if (schema.Enum != null)
        {
            if (!MatchesEnum(el, schema.Enum))
            {
                errors.Add($"{path} 不在允许的枚举范围内");
                return; // 枚举不命中即终止该节点检查
            }
        }

        switch (schema.Type)
        {
            case "object":
                if (el.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{path} 应为 object，实际 {el.ValueKind}");
                    return;
                }
                if (schema.Required != null)
                {
                    foreach (var name in schema.Required)
                    {
                        if (!el.TryGetProperty(name, out _))
                            errors.Add($"{path} 缺少必填属性 \"{name}\"");
                    }
                }
                if (schema.Properties != null)
                {
                    foreach (var (name, childSchema) in schema.Properties)
                    {
                        if (el.TryGetProperty(name, out var child))
                            ValidateElement(child, childSchema, $"{path}.{name}", errors);
                    }
                }
                break;

            case "array":
                if (el.ValueKind != JsonValueKind.Array)
                {
                    errors.Add($"{path} 应为 array，实际 {el.ValueKind}");
                    return;
                }
                var count = 0;
                foreach (var item in el.EnumerateArray())
                {
                    ValidateElement(item, schema.Items, $"{path}[{count}]", errors);
                    count++;
                }
                if (schema.MinItems.HasValue && count < schema.MinItems.Value)
                    errors.Add($"{path} 数组长度 {count} < minItems {schema.MinItems.Value}");
                if (schema.MaxItems.HasValue && count > schema.MaxItems.Value)
                    errors.Add($"{path} 数组长度 {count} > maxItems {schema.MaxItems.Value}");
                break;

            case "string":
                if (el.ValueKind != JsonValueKind.String)
                    errors.Add($"{path} 应为 string，实际 {el.ValueKind}");
                break;

            case "integer":
                if (!el.TryGetInt64(out _))
                    errors.Add($"{path} 应为 integer，实际 {el.ValueKind}");
                else
                    ValidateNumberRange(el, schema, path, errors);
                break;

            case "number":
                if (el.ValueKind != JsonValueKind.Number)
                    errors.Add($"{path} 应为 number，实际 {el.ValueKind}");
                else
                    ValidateNumberRange(el, schema, path, errors);
                break;

            case "boolean":
                if (el.ValueKind != JsonValueKind.True && el.ValueKind != JsonValueKind.False)
                    errors.Add($"{path} 应为 boolean，实际 {el.ValueKind}");
                break;

            case "null":
                if (el.ValueKind != JsonValueKind.Null)
                    errors.Add($"{path} 应为 null，实际 {el.ValueKind}");
                break;

            default:
                // 无 Type（游标节点）——仅向下递归（如 items 懒节点）
                break;
        }
    }

    private static void ValidateNumberRange(JsonElement el, JsonSchema schema, string path, List<string> errors)
    {
        if (!el.TryGetDecimal(out var value))
            return;
        if (schema.Minimum.HasValue && value < schema.Minimum.Value)
            errors.Add($"{path} 数值 {value} < minimum {schema.Minimum.Value}");
        if (schema.Maximum.HasValue && value > schema.Maximum.Value)
            errors.Add($"{path} 数值 {value} > maximum {schema.Maximum.Value}");
    }

    private static bool MatchesEnum(JsonElement el, IReadOnlyList<object> allowed)
    {
        foreach (var candidate in allowed)
        {
            if (TryScalarEquals(el, candidate))
                return true;
        }
        return false;
    }

    private static bool TryScalarEquals(JsonElement el, object candidate)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                return candidate is string s && s == el.GetString();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return candidate is bool b && b == el.GetBoolean();
            case JsonValueKind.Number:
                return candidate switch
                {
                    int i => el.TryGetInt32(out var v) && v == i,
                    long l => el.TryGetInt64(out var v) && v == l,
                    double d => el.TryGetDouble(out var v) && v.Equals(d),
                    decimal m => el.TryGetDecimal(out var v) && v == m,
                    _ => false,
                };
            case JsonValueKind.Null:
                return candidate == null;
            default:
                return false;
        }
    }
}