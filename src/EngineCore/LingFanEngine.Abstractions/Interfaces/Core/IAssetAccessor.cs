namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 资源访问器抽象——将平台资源加载逻辑从引擎核心中解耦。
/// <para>引擎核心通过此接口访问内嵌资源（avares://）和文件系统资源，不直接依赖 Avalonia。</para>
/// <para>实现方在组合根注册（如 AvaloniaAssetAccessor 封装 AssetLoader + 文件系统回退）。</para>
/// </summary>
public interface IAssetAccessor
{
    /// <summary>
    /// 尝试打开资源流。
    /// <para>优先从内嵌资源读取，失败时回退文件系统。</para>
    /// </summary>
    /// <param name="path">资源路径（物理路径或 avares:// URI）。</param>
    /// <returns>资源流，如果资源不存在则返回 null。</returns>
    Stream? Open(string path);

    /// <summary>
    /// 读取文本资源。
    /// </summary>
    /// <param name="path">资源路径。</param>
    /// <returns>文本内容，如果资源不存在则返回 null。</returns>
    string? ReadText(string path);

    /// <summary>
    /// 检查资源是否存在。
    /// </summary>
    /// <param name="path">资源路径。</param>
    /// <returns>存在返回 true，否则 false。</returns>
    bool Exists(string path);
}
