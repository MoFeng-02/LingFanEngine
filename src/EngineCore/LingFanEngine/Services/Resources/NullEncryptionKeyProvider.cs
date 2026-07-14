using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Resources;

/// <summary>
/// 默认密钥提供者——开发期返回 null（无加密）。
/// <para>游戏层应注册自己的 IEncryptionKeyProvider 实现以提供构建时注入的密钥。</para>
/// </summary>
internal sealed class NullEncryptionKeyProvider : IEncryptionKeyProvider
{
    public byte[]? GetKey() => null;
}
