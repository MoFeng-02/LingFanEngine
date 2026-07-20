namespace LingFanEngine.SDK.Constants;

/// <summary>
/// SDK 项目常量——集中管理所有目录名、文件名、扩展名等魔法字符串。
/// <para>所有 SDK 代码应引用此常量类，避免散落的硬编码字符串。</para>
/// </summary>
public static class ProjectConstants
{
    // ── 资源目录名 ──────────────────────────────────────────────

    /// <summary>
    /// 项目根共享资源目录名。
    /// <para>所有资源（Stories/Media/Images/Audio/Video/Lang/Live2D）集中在此目录下，
    /// 由 Directory.Build.props 的 Content 项统一复制到可执行项目输出。</para>
    /// </summary>
    public const string ResourcesDir = "Resources";

    /// <summary>故事脚本目录名</summary>
    public const string StoriesDir = "Stories";

    /// <summary>媒体资源目录名（含 BGM/SE/Voice/Images/Video 子目录）</summary>
    public const string MediaDir = "Media";

    /// <summary>图片资源目录名</summary>
    public const string ImagesDir = "Images";

    /// <summary>音频资源目录名</summary>
    public const string AudioDir = "Audio";

    /// <summary>视频资源目录名</summary>
    public const string VideoDir = "Video";

    /// <summary>Live2D 资源目录名</summary>
    public const string Live2DDir = "Live2D";

    /// <summary>语言文件目录名</summary>
    public const string LangDir = "Lang";

    /// <summary>图标目录名</summary>
    public const string IconsDir = "Icons";

    /// <summary>Avalonia 资源目录名（编译进 AvaloniaResource）</summary>
    public const string AssetsDir = "Assets";

    /// <summary>
    /// 所有资源目录名（与 .csproj 的 None Include 对应）。
    /// <para>这些目录的内容会被 dotnet publish 复制到输出目录。</para>
    /// </summary>
    public static readonly string[] ResourceDirNames =
    [
        StoriesDir, MediaDir, ImagesDir, AudioDir,
        VideoDir, Live2DDir, LangDir,
    ];

    /// <summary>
    /// 所有需要确保存在的子目录（含 Media 下的子分类）。
    /// <para>用于项目打开/创建时自动创建标准目录结构。</para>
    /// </summary>
    public static readonly string[] StandardSubDirs =
    [
        StoriesDir,
        MediaDir,
        $"{MediaDir}/BGM",
        $"{MediaDir}/SE",
        $"{MediaDir}/Voice",
        $"{MediaDir}/{ImagesDir}",
        $"{MediaDir}/{VideoDir}",
        ImagesDir,
        AudioDir,
        VideoDir,
        Live2DDir,
        LangDir,
    ];

    /// <summary>
    /// 资源管理器扫描的目录列表。
    /// <para>AssetManager 扫描这些目录下的文件并分类展示。</para>
    /// </summary>
    public static readonly string[] AssetScanDirs =
    [
        StoriesDir, MediaDir, AssetsDir,
    ];

    // ── Media 子目录名 ──────────────────────────────────────────

    /// <summary>BGM 子目录名</summary>
    public const string BGMSubDir = "BGM";

    /// <summary>音效子目录名</summary>
    public const string SESubDir = "SE";

    /// <summary>语音子目录名</summary>
    public const string VoiceSubDir = "Voice";

    // ── 项目结构目录名 ──────────────────────────────────────────

    /// <summary>安全目录名（GeneratedKeys.cs 等安全相关代码）</summary>
    public const string SecurityDir = "Security";

    /// <summary>DLL 引用目录名（引擎 DLL 存放位置）</summary>
    public const string DllDir = "DLL";

    /// <summary>发布输出目录名</summary>
    public const string PublishDir = "publish";

    // ── 文件名 ──────────────────────────────────────────────────

    /// <summary>生成的密钥代码文件名</summary>
    public const string GeneratedKeysFileName = "GeneratedKeys.cs";

    /// <summary>加密清单文件名</summary>
    public const string ManifestFileName = "manifest.lfmanifest";

    /// <summary>项目文件扩展名</summary>
    public const string CsprojExt = ".csproj";

    /// <summary>C# 源文件扩展名</summary>
    public const string CsExt = ".cs";

    /// <summary>故事脚本文件扩展名</summary>
    public const string StoryExt = ".story";

    /// <summary>JSON 文件扩展名</summary>
    public const string JsonExt = ".json";

    /// <summary>引擎版本锁定文件名（每项目/缓存内声明版本真相，AOT 安全 JSON）。</summary>
    public const string EngineLockFileName = "engine.lock.json";

    /// <summary>引擎缓存目录名（SDK 已知最新引擎 DLL 集，离线建项目/预览的种子源）。</summary>
    public const string EngineCacheDir = "engine-cache";

    // ── 引擎 DLL 名称 ───────────────────────────────────────────

    /// <summary>引擎核心 DLL</summary>
    public const string EngineCoreDll = "LingFanEngine.dll";

    /// <summary>引擎抽象层 DLL</summary>
    public const string EngineAbstractionsDll = "LingFanEngine.Abstractions.dll";

    /// <summary>DSL 共享解析层 DLL</summary>
    public const string EngineDslCoreDll = "LingFanEngine.DslCore.dll";

    /// <summary>解析器库 DLL</summary>
    public const string PidginDll = "Pidgin.dll";

    /// <summary>
    /// SDK 分发的引擎 DLL 列表（含运行时引擎核心 LingFanEngine.dll）。
    /// <para>PublishService.UpdateEngineDlls 从 SDK 输出目录复制这些 DLL（4 个）到用户项目。</para>
    /// <para>包含 LingFanEngine.dll 使发布具备自愈能力：用户项目缺失或引擎更新时，
    /// 发布会自动补齐/覆盖最新引擎核心，不再依赖模板创建时带入。</para>
    /// </summary>
    public static readonly string[] SdkDistributedDlls =
    [
        EngineCoreDll, EngineAbstractionsDll, EngineDslCoreDll, PidginDll,
    ];

    // ── 构建相关 ────────────────────────────────────────────────

    /// <summary>MSBuild 属性：不生成调试符号</summary>
    public const string MsbuildDebugTypeNone = "DebugType=none";

    /// <summary>MSBuild 属性：不生成调试符号文件</summary>
    public const string MsbuildDebugSymbolsFalse = "DebugSymbols=false";

    /// <summary>
    /// 加密时排除的非资源目录名（dotnet publish 产物中的框架/运行时目录）。
    /// </summary>
    public static readonly string[] EncryptExcludeDirs =
    [
        "runtimes", "ref", "refs", "bin", "obj",
    ];

    // ── 平台 .csproj 后缀 ─────────────────────────────────────

    public const string CsprojSuffixWindows = ".Desktop.Windows.csproj";
    public const string CsprojSuffixLinux = ".Desktop.Linux.csproj";
    public const string CsprojSuffixMac = ".Desktop.Mac.csproj";
    public const string CsprojSuffixAndroid = ".Android.csproj";
    public const string CsprojSuffixIOS = ".iOS.csproj";
    public const string CsprojSuffixBrowser = ".Browser.csproj";
    public const string CsprojSuffixDefault = ".csproj";

    // ── DI 注册关键词 ──────────────────────────────────────────

    /// <summary>引擎 DI 注册方法名（用于扫描用户代码中的注册入口）</summary>
    public const string AddLingFanEngineMethod = "AddLingFanEngine";

    /// <summary>生成的密钥提供者类名（用于检测是否已注册）</summary>
    public const string GeneratedKeyProviderClass = "GeneratedKeyProvider";

    /// <summary>IEncryptionKeyProvider 接口所在命名空间</summary>
    public const string EncryptionKeyProviderNamespace = "LingFanEngine.Abstractions.Interfaces.Core";

    // ── 文件扩展名分类 ──────────────────────────────────────────

    /// <summary>图片文件扩展名（用于资源分类和导入）</summary>
    public static readonly string[] ImageExtensions =
    [
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp",
    ];

    /// <summary>音频文件扩展名（用于资源分类和导入）</summary>
    public static readonly string[] AudioExtensions =
    [
        ".mp3", ".ogg", ".wav", ".flac", ".m4a",
    ];

    /// <summary>视频文件扩展名（用于资源分类和导入）</summary>
    public static readonly string[] VideoExtensions =
    [
        ".mp4", ".webm", ".avi", ".mkv",
    ];

    /// <summary>
    /// 拖拽导入时根据扩展名映射的目标子目录。
    /// <para>未匹配的扩展名导入到 Media 根目录。</para>
    /// </summary>
    public static string GetImportTargetSubDir(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => ImagesDir,
        ".mp3" or ".ogg" or ".wav" => BGMSubDir,
        ".mp4" or ".webm" or ".mkv" => VideoDir,
        _ => "",
    };
}
