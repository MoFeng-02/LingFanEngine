using System.Text.Json;
using System.Text.Json.Serialization;

namespace LingFanEngine.Abstractions.Serialization;

/// <summary>
/// 自定义 JsonConverter，处理 Dictionary&lt;string, object&gt; 在 JsonAOT 下的序列化/反序列化。
/// <para>UIElementEntity.Properties 等字段使用此转换器保证 AOT 兼容。</para>
/// </summary>
public class ObjectDictionaryConverter : JsonConverter<Dictionary<string, object?>>
{
    public override Dictionary<string, object?>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token");

        var result = new Dictionary<string, object?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Expected PropertyName token");

            var key = reader.GetString();
            reader.Read();
            result[key!] = ReadValue(ref reader);
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object?> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kv in value)
        {
            writer.WritePropertyName(kv.Key);
            WriteValue(writer, kv.Value, options);
        }
        writer.WriteEndObject();
    }

    private static object? ReadValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var intVal))
                    return intVal;
                if (reader.TryGetInt64(out var longVal))
                    return longVal;
                return reader.GetDouble();
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.StartObject:
                // 简单对象递归（只支持扁平 string->value 结构）
                var obj = new Dictionary<string, object?>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        return obj;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var key = reader.GetString();
                        reader.Read();
                        obj[key!] = ReadValue(ref reader);
                    }
                }
                return obj;
            case JsonTokenType.StartArray:
                var list = new List<object?>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        return list;
                    list.Add(ReadValue(ref reader));
                }
                return list;
            default:
                return null;
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case string s:
                writer.WriteStringValue(s);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case Dictionary<string, object?> dict:
                writer.WriteStartObject();
                foreach (var kv in dict)
                {
                    writer.WritePropertyName(kv.Key);
                    WriteValue(writer, kv.Value, options);
                }
                writer.WriteEndObject();
                break;
            case List<object?> list:
                writer.WriteStartArray();
                foreach (var item in list)
                    WriteValue(writer, item, options);
                writer.WriteEndArray();
                break;
            default:
                // AOT 安全回退：不使用 JsonSerializer.Serialize(writer, value, value.GetType())
                // 因为 GetType() 反射在 NativeAOT 下会抛 UnsupportedTypeException
                // 改为写入 ToString() 字符串，保证不崩溃
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
