using System.Threading;
using System.Threading.Tasks;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 加密文件读取器——即解即用模式。
/// <para>读取文件时自动检测 LFEN 魔数头：加密则解密后返回明文，不加密则直接返回原始数据。</para>
/// <para>无密钥时（开发期）跳过解密检测，直接读取文件。</para>
/// <para>文本类资源（.story/.json）从内存使用；音频/视频通过 TryDecryptToFile 解密到临时文件。</para>
/// </summary>
public interface IEncryptedFileReader
{
    /// <summary>
    /// 读取文件全部字节（自动检测并解密 LFEN 格式）。
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>文件内容（已解密），文件不存在返回 null</returns>
    ValueTask<byte[]?> ReadAllBytesAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// 读取文件全部文本（自动检测并解密 LFEN 格式，UTF-8 解码）。
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>文件文本（已解密），文件不存在返回 null</returns>
    ValueTask<string?> ReadAllTextAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// 打开文件流（自动检测并解密 LFEN 格式）。
    /// <para>加密文件解密后返回 MemoryStream；未加密文件返回 FileStream。</para>
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <returns>文件流（已解密），文件不存在返回 null</returns>
    Stream? OpenRead(string path);

    /// <summary>
    /// 尝试解密文件到临时文件路径（音频/视频用）。
    /// <para>如果文件已加密，解密到临时文件并返回临时路径；如果不加密，直接返回原始路径。</para>
    /// <para>调用方使用完毕后应调用 ReleaseTempFile 释放。</para>
    /// </summary>
    /// <param name="path">原始文件路径</param>
    /// <returns>(临时文件路径或原始路径, 是否为临时文件)</returns>
    (string path, bool isTemp) TryDecryptToFile(string path);

    /// <summary>
    /// 释放临时解密文件。
    /// </summary>
    /// <param name="tempPath">TryDecryptToFile 返回的路径</param>
    /// <param name="isTemp">TryDecryptToFile 返回的 isTemp 标志</param>
    void ReleaseTempFile(string tempPath, bool isTemp);

    /// <summary>
    /// 检查文件是否为 LFEN 加密格式。
    /// </summary>
    bool IsEncrypted(string path);
}
