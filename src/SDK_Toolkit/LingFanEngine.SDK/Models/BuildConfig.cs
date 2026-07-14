namespace LingFanEngine.SDK.Models;

/// <summary>构建配置</summary>
public class BuildConfig
{
    /// <summary>配置类型（Debug/Release）</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>是否自包含运行时</summary>
    public bool SelfContained { get; set; } = false;

    /// <summary>裁剪模式（none/partial/full）</summary>
    public string? TrimMode { get; set; }

    /// <summary>是否使用 AOT 发布</summary>
    public bool PublishAot { get; set; } = true;

    /// <summary>
    /// 是否加密资源（Phase 50：即解即用模式）。
    /// <para>true = publish 后按 EncryptionConfig.EncryptFileTypes 原地加密文件。</para>
    /// <para>false = 不加密，资源明文部署（开发期推荐）。</para>
    /// </summary>
    public bool EncryptResources { get; set; } = false;

    /// <summary>输出路径（相对于项目目录）</summary>
    public string OutputPath { get; set; } = "publish";
}
