using LibVLCSharp.Shared;

namespace LingFanEngine.Services.Media;

/// <summary>
/// LibVLC 跨平台初始化器——线程安全单例
/// <para>Core.Initialize() 需在平台入口调用（App.axaml.cs / Program.cs）。</para>
/// <para>LibVLC 实例全局共享，所有播放器复用同一引擎实例。</para>
/// <para>Browser/WASM 平台不支持 LibVLC，降级为 NullAsyncAudioPlayer。</para>
/// </summary>
public static class LibVlcInitializer
{
    private static LibVLC? _instance;
    private static readonly object _lock = new();
    private static bool _coreInitialized;

    /// <summary>LibVLC 是否可用（非 Browser 平台且初始化成功）</summary>
    public static bool IsAvailable { get; private set; }

    /// <summary>
    /// 初始化 Core（平台入口调用，仅一次）
    /// <para>在 Browser/WASM 平台自动跳过。</para>
    /// </summary>
    public static void InitializeCore()
    {
        if (_coreInitialized) return;
        _coreInitialized = true;

        // Browser/WASM 不支持 LibVLC
        if (OperatingSystem.IsBrowser())
        {
            IsAvailable = false;
            return;
        }

        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibVlcInitializer] Core.Initialize 失败: {ex.Message}");
            IsAvailable = false;
        }
    }

    /// <summary>
    /// 获取全局 LibVLC 实例（线程安全单例）
    /// <para>若 LibVLC 不可用则返回 null。</para>
    /// </summary>
    public static LibVLC? GetLibVLC()
    {
        if (!IsAvailable) return null;

        lock (_lock)
        {
            _instance ??= new LibVLC(
                enableDebugLogs: false,
                // 禁用视频输出（纯音频模式，减少资源开销）
                options: ["--no-video", "--no-osd"]);
            return _instance;
        }
    }

    /// <summary>释放 LibVLC 实例（应用关闭时调用）</summary>
    public static void Dispose()
    {
        lock (_lock)
        {
            _instance?.Dispose();
            _instance = null;
        }
    }
}
