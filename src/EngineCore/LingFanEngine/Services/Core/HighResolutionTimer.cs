using System.Runtime.InteropServices;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 跨平台定时器精度管理。
/// <para>Windows: 通过 timeBeginPeriod(1) 提升系统定时器分辨率至 1ms（Activate/Deactivate 引用计数管理）。</para>
/// <para>Linux/macOS: 原生定时器已 ~1ms（clock_nanosleep），Activate/Deactivate 为 no-op。</para>
/// <para>Android/iOS: 原生定时器已 ~1ms，Activate/Deactivate 为 no-op。自旋策略更保守（SpinMarginMs=1, SpinYieldInterval=4）以省电减发热。</para>
/// <para>WASM/Browser: setTimeout 精度 ~4ms，Activate/Deactivate 为 no-op。</para>
/// <para>DelayPrecisionMs/SpinMarginMs/SpinYieldInterval 为所有平台提供正确的节流参数，GameLoop 据此决定何时让出 CPU。</para>
/// <para>AOT 安全：[DllImport] 是编译期解析的原生调用，OperatingSystem.IsWindows() 运行时守卫，零反射。</para>
/// <para>副作用：timeBeginPeriod(1) 会提高系统定时器中断频率。Windows 10 v2004+ 已限制为进程级影响。</para>
/// </summary>
public static class HighResolutionTimer
{
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uPeriod);

    /// <summary>Windows 平台标志——类初始化时一次性检测，避免每次调用重复判断。</summary>
    private static readonly bool s_isWindows = OperatingSystem.IsWindows();

    /// <summary>引用计数——线程安全，Interlocked 操作。</summary>
    private static int s_refCount;

    /// <summary>
    /// 高精度定时器是否生效（仅 Windows 且 timeBeginPeriod 成功时为 true）
    /// </summary>
    public static bool IsActive => s_refCount > 0;

    /// <summary>
    /// 当前平台的 Task.Delay 最小有效精度（毫秒），用于 GameLoop 决定何时让出 CPU。
    /// <para>Windows + timeBeginPeriod(1): ~1ms → 阈值 3ms</para>
    /// <para>Windows 默认: ~15.6ms → 阈值 16ms（避免超睡拖低帧率）</para>
    /// <para>Linux/macOS: ~1ms（clock_nanosleep 原生高精度）→ 阈值 3ms</para>
    /// <para>Android/iOS: ~1ms（原生高精度）→ 阈值 3ms</para>
    /// <para>WASM/Browser: ~4ms（setTimeout 最小钳位）→ 阈值 6ms</para>
    /// </summary>
    public static int DelayPrecisionMs
    {
        get
        {
            if (s_isWindows)
                return s_refCount > 0 ? 3 : 16;
            if (OperatingSystem.IsBrowser())
                return 6;
            // Linux / macOS / iOS / Android — 原生定时器精度已 ~1ms
            return 3;
        }
    }

    /// <summary>
    /// Task.Delay 后保留给自旋等待的安全边际（毫秒）。
    /// <para>Desktop (Windows/Linux/macOS): 2ms — 追求帧率精度</para>
    /// <para>Mobile (Android/iOS): 1ms — 省电优先，接受 ~1ms 帧率抖动</para>
    /// <para>WASM: 4ms — setTimeout 精度差，需更大缓冲</para>
    /// </summary>
    public static int SpinMarginMs
    {
        get
        {
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
                return 1;
            if (OperatingSystem.IsBrowser())
                return 4;
            return 2; // Desktop
        }
    }

    /// <summary>
    /// 自旋等待中 Thread.Sleep(0) 的让出频率（每 N 次自旋让出一次 CPU）。
    /// <para>Desktop: 16 — 自旋效率高，少量让出即可</para>
    /// <para>Mobile: 4 — 频繁让出，减少发热和电池消耗</para>
    /// <para>WASM: 8 — 中间值</para>
    /// </summary>
    public static int SpinYieldInterval
    {
        get
        {
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
                return 4;
            if (OperatingSystem.IsBrowser())
                return 8;
            return 16; // Desktop
        }
    }

    /// <summary>
    /// 激活高精度定时器（引用计数 +1）
    /// <para>Windows: 调用 timeBeginPeriod(1) 将系统定时器分辨率提升至 1ms。</para>
    /// <para>其他平台: no-op。</para>
    /// <para>失败时（如 winmm.dll 不可用）回退计数，IsActive 返回 false，GameLoop 退回 16ms 阈值。</para>
    /// </summary>
    public static void Activate()
    {
        if (!s_isWindows)
            return;

        if (Interlocked.Increment(ref s_refCount) == 1)
        {
            try
            {
                var result = TimeBeginPeriod(1);
                if (result != 0) // TIMERR_NOCANDO (97) = 不支持
                {
                    // 失败：回退引用计数，让 IsActive 返回 false
                    Interlocked.Decrement(ref s_refCount);
                    System.Diagnostics.Debug.WriteLine(
                        $"[HighResolutionTimer] timeBeginPeriod(1) returned {result}, falling back to default timer resolution");
                }
            }
            catch (Exception ex)
            {
                // winmm.dll 不可用或其他异常：回退计数
                Interlocked.Decrement(ref s_refCount);
                System.Diagnostics.Debug.WriteLine(
                    $"[HighResolutionTimer] timeBeginPeriod(1) failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 停用高精度定时器（引用计数 -1，归零时恢复系统默认分辨率）
    /// </summary>
    public static void Deactivate()
    {
        if (!s_isWindows)
            return;

        var newCount = Interlocked.Decrement(ref s_refCount);
        if (newCount == 0)
        {
            try
            {
                TimeEndPeriod(1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[HighResolutionTimer] timeEndPeriod(1) failed: {ex.Message}");
            }
        }
        else if (newCount < 0)
        {
            // 防御：不应出现负数，修正为 0
            Interlocked.Exchange(ref s_refCount, 0);
        }
    }
}
