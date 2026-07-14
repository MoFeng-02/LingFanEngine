using System.Collections.Generic;

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

    /// <summary>
    /// 要加密的文件扩展名列表（点分小写，如 ".story"、".png"）。
    /// <para>空列表 = 不加密任何文件。null = 加密所有已知资源类型（全选）。</para>
    /// <para>Phase 50：即解即用模式，加密的文件原地替换为 LFEN 格式，运行时自动检测解密。</para>
    /// </summary>
    public List<string>? EncryptFileTypes { get; set; } = new()
    {
        ".story", ".json",
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".mp3", ".ogg", ".wav",
        ".mp4", ".webm", ".mkv",
    };

    /// <summary>
    /// 是否生成加密清单文件（manifest.lfmanifest）。
    /// <para>清单记录所有加密文件的相对路径 + SHA256 校验。清单本身也可加密。</para>
    /// </summary>
    public bool GenerateManifest { get; set; } = true;

    /// <summary>是否加密清单文件本身</summary>
    public bool EncryptManifest { get; set; } = true;

    /// <summary>
    /// 所有可加密的资源文件扩展名（供 UI 勾选用）
    /// </summary>
    public static readonly IReadOnlyList<string> AllEncryptableTypes =
    [
        ".story", ".json",
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".mp3", ".ogg", ".wav",
        ".mp4", ".webm", ".mkv",
    ];
}
