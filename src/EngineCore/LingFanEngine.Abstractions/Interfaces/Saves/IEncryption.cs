namespace LingFanEngine.Abstractions.Interfaces.Saves;

/// <summary>
/// 加密接口
/// <para>开发者可自定义加密逻辑，如 AES、RSA、自定义算法等</para>
/// </summary>
public interface IEncryption
{
    /// <summary>
    /// 加密数据
    /// </summary>
    /// <param name="data">原始数据</param>
    /// <returns>加密后数据</returns>
    byte[] Encrypt(byte[] data);

    /// <summary>
    /// 解密数据
    /// </summary>
    /// <param name="data">加密后数据</param>
    /// <returns>原始数据</returns>
    byte[] Decrypt(byte[] data);
}