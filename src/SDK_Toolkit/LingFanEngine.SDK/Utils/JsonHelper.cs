using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Utils;

/// <summary>
/// AOT 友好的 JSON 序列化上下文（source generator）。
/// <para>所有需要 JSON 序列化的类型必须在此注册，编译时生成零反射的 JsonTypeInfo。</para>
/// </summary>
[JsonSerializable(typeof(ProjectConfig))]
[JsonSerializable(typeof(List<RecentProject>))]
[JsonSerializable(typeof(BuildResult))]
[JsonSerializable(typeof(DslAnalysisResult))]
[JsonSerializable(typeof(List<DslDiagnostic>))]
[JsonSerializable(typeof(List<VariableInfo>))]
[JsonSerializable(typeof(List<SceneReference>))]
[JsonSerializable(typeof(PackManifestInfo))]
[JsonSerializable(typeof(PackResult))]
[JsonSerializable(typeof(SdkSettings))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class SdkJsonContext : JsonSerializerContext;

/// <summary>
/// AOT 友好的 JSON 序列化辅助类。
/// <para>所有方法通过 SdkJsonContext 的 source-generated JsonTypeInfo 调用，零反射。</para>
/// <para>泛型方法需调用方显式传入 <see cref="JsonTypeInfo{T}"/>，编译时即可确定类型信息。</para>
/// </summary>
public static class JsonHelper
{
    /// <summary>序列化为 JSON 字符串（AOT 安全）</summary>
    /// <param name="obj">要序列化的对象</param>
    /// <param name="typeInfo">source-generated 类型信息</param>
    public static string Serialize<T>(T obj, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(obj, typeInfo);

    /// <summary>从 JSON 字符串反序列化（AOT 安全）</summary>
    /// <param name="json">JSON 字符串</param>
    /// <param name="typeInfo">source-generated 类型信息</param>
    public static T? Deserialize<T>(string json, JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Deserialize(json, typeInfo);

    /// <summary>异步序列化到文件（AOT 安全）</summary>
    /// <param name="obj">要序列化的对象</param>
    /// <param name="filePath">目标文件路径</param>
    /// <param name="typeInfo">source-generated 类型信息</param>
    public static async Task SerializeToFileAsync<T>(T obj, string filePath, JsonTypeInfo<T> typeInfo)
    {
        var json = Serialize(obj, typeInfo);
        await FileHelper.WriteAllTextAsync(filePath, json);
    }

    /// <summary>从文件异步反序列化（AOT 安全）</summary>
    /// <param name="filePath">源文件路径</param>
    /// <param name="typeInfo">source-generated 类型信息</param>
    public static async Task<T?> DeserializeFromFileAsync<T>(string filePath, JsonTypeInfo<T> typeInfo)
    {
        if (!FileHelper.FileExists(filePath))
            return default;
        var json = await FileHelper.ReadAllTextAsync(filePath);
        return Deserialize(json, typeInfo);
    }
}
