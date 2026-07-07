using System.Text.Json;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// JSON 值转换器接口
/// <para>将存档反序列化后的 JsonElement 值转换为 .NET 原生类型。</para>
/// <para>支持注册自定义转换器，优先级高于默认转换链。</para>
/// </summary>
public interface IJsonValueConverter
{
    /// <summary>
    /// 将运行时值（可能是 JsonElement）转换为 .NET 原生类型
    /// </summary>
    object? Convert(object? value);

    /// <summary>
    /// 注册自定义 JsonElement → .NET 类型转换器。
    /// <para>注册的转换器插入默认转换链之前，优先级最高。返回 null 表示不处理，继续走链路。</para>
    /// </summary>
    void RegisterCustomConverter(Func<JsonElement, object?> converter);
}
