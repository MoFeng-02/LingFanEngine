namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 加密密钥提供者——游戏层实现此接口返回构建时注入的密钥。
/// <para>典型实现：从 GeneratedKeys.GetKey() 返回分片重组后的 32 字节密钥。</para>
/// <para>开发期（未加密资源）可返回 null 或不注册此接口。</para>
/// </summary>
public interface IEncryptionKeyProvider
{
    /// <summary>
    /// 获取 AES-256 密钥（32 字节），或 null 表示无密钥（开发期）。
    /// </summary>
    byte[]? GetKey();
}
