namespace LingFanEngine.SDK.Models;

/// <summary>加密配置</summary>
public class EncryptionConfig
{
    /// <summary>是否启用加密</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>加密算法</summary>
    public string Algorithm { get; set; } = "AES-256-GCM";

    /// <summary>密钥分片数（分散存储增加安全性）</summary>
    public int KeyShardCount { get; set; } = 4;
}
