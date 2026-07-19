using LibVLCSharp.Shared;

namespace LingFanEngine.Services.Media;

/// <summary>
/// LibVLC 跨平台初始化器——线程安全单例
/// <para>Core.Initialize() 需在平台入口调用（App.axaml.cs / Program.cs）。</para>
/// <para>LibVLC 实例全局共享，所有播放器复用同一引擎实例。</para>
/// <para>Browser/WASM 平台不支持 LibVLC，降级为 NullAsyncAudioPlayer。</para>
/// <para>Phase 64：Lazy&lt;T&gt; 替代手写 lock 单例——框架保证线程安全延迟初始化。</para>
/// </summary>
public static class LibVlcInitializer
{
    /// <summary>LibVLC 是否可用（非 Browser 平台且初始化成功）</summary>
    public static bool IsAvailable { get; private set; }

    private static bool _coreInitialized;

    /// <summary>全局 LibVLC 实例（Lazy 延迟初始化，线程安全）</summary>
    private static readonly Lazy<LibVLC?> _instance = new(() =>
    {
        if (!IsAvailable) return null;
        try
        {
            return new LibVLC(
                enableDebugLogs: false,
                // 禁用视频输出（纯音频模式，减少资源开销）
                options: ["--no-video", "--no-osd"]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibVlcInitializer] 创建 LibVLC 实例失败: {ex.Message}");
            return null;
        }
    });

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
    public static LibVLC? GetLibVLC() => _instance.Value;

    /// <summary>释放 LibVLC 实例（应用关闭时调用）</summary>
    public static void Dispose()
    {
        if (_instance.IsValueCreated)
        {
            try { _instance.Value?.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LibVlcInitializer] Dispose 失败: {ex.Message}"); }
        }
    }
}
