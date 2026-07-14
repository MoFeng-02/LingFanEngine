using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>
/// 资源打包服务接口
/// <para>将游戏资源（Stories/Media 等）打包为 .lfpack 加密包。</para>
/// <para>.lfpack 格式：魔数(4) + manifest长度(4) + manifest JSON + AES 加密 ZIP 数据</para>
/// </summary>
public interface IPackToolService
{
    /// <summary>
    /// 打包目录为 .lfpack 文件
    /// </summary>
    /// <param name="sourceDir">源目录（如 Stories/ 或 Media/）</param>
    /// <param name="outputPath">输出 .lfpack 文件路径</param>
    /// <param name="key">AES-256 密钥（32 字节）</param>
    /// <param name="manifest">可选清单（null=自动生成）</param>
    /// <param name="progress">进度回调</param>
    /// <returns>打包结果</returns>
    Task<PackResult> PackAsync(
        string sourceDir,
        string outputPath,
        byte[] key,
        PackManifestInfo? manifest = null,
        IProgress<string>? progress = null);

    /// <summary>
    /// 列出 .lfpack 包中的文件列表（不解包，无需密钥）
    /// </summary>
    /// <param name="packPath">.lfpack 文件路径</param>
    /// <returns>包清单信息，失败返回 null</returns>
    Task<PackManifestInfo?> ListAsync(string packPath);

    /// <summary>
    /// 解包 .lfpack 到目录
    /// </summary>
    /// <param name="packPath">.lfpack 文件路径</param>
    /// <param name="outputDir">输出目录</param>
    /// <param name="key">AES-256 密钥</param>
    /// <param name="progress">进度回调</param>
    /// <returns>解包结果</returns>
    Task<PackResult> UnpackAsync(
        string packPath,
        string outputDir,
        byte[] key,
        IProgress<string>? progress = null);
}
