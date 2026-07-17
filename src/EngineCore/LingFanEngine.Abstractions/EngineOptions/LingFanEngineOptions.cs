using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.EngineOptions;

/// <summary>
/// 布局缩放模式——控制设计分辨率到实际窗口的缩放策略（Phase 27）
/// </summary>
public enum LayoutScaleMode
{
    /// <summary>
    /// 等比缩放，内容完全可见，不足部分留黑边（对标 Ren'Py 默认行为）
    /// <para>公式：scale = Math.Min(scaleX, scaleY)</para>
    /// <para>窗口宽高比与设计分辨率不一致时，短边方向留黑边</para>
    /// </summary>
    Contain,

    /// <summary>
    /// 等比缩放，填满整个窗口，超出的边缘被裁切
    /// <para>公式：scale = Math.Max(scaleX, scaleY)</para>
    /// <para>窗口宽高比与设计分辨率不一致时，长边方向裁切</para>
    /// </summary>
    Cover,

    /// <summary>
    /// 独立 X/Y 缩放，填满整个窗口，不裁切不黑边，但可能轻微变形
    /// <para>公式：scaleX 和 scaleY 各自独立</para>
    /// <para>窗口标题栏等导致的几像素差异几乎不可见，适合 VN 游戏</para>
    /// </summary>
    Stretch
}

/// <summary>
/// 引擎配置选项
/// <para>集中管理引擎的运行参数，包括平台适配、性能、路径等配置。</para>
/// <para>已迁移至 Abstractions 层，便于接口定义引用。</para>
/// </summary>
public class LingFanEngineOptions
{
    // ── 路径配置 ──

    /// <summary>
    /// 存档存储目录（默认 "Saves"）
    /// </summary>
    public string SaveDirectory { get; set; } = "Saves";

    /// <summary>
    /// 媒体资源根目录（默认 "Media"）
    /// </summary>
    public string MediaDirectory { get; set; } = "Media";

    /// <summary>
    /// Live2D 模型根目录（默认 "Live2D"）
    /// </summary>
    public string Live2DDirectory { get; set; } = "Live2D";

    /// <summary>
    /// Mods/DLC 目录（默认 "Mods"）
    /// </summary>
    public string ModsDirectory { get; set; } = "Mods";

    /// <summary>
    /// 故事脚本根目录（默认 "Stories"）
    /// <para>StoryRegistry.Scan() 在此目录下递归扫描 .story 文件。</para>
    /// </summary>
    public string StoriesDirectory { get; set; } = "Stories";

    // ── 性能配置 ──

    /// <summary>
    /// 桌面目标帧率（默认 120）
    /// </summary>
    public int DesktopTargetFps { get; set; } = 120;

    /// <summary>
    /// 移动端目标帧率（默认 60）
    /// </summary>
    public int MobileTargetFps { get; set; } = 60;

    /// <summary>
    /// 渲染缩放系数（移动端高温降频时自动降低）
    /// </summary>
    public double RenderScale { get; set; } = 1.0;

    // ── 平台配置 ──

    /// <summary>
    /// 窗口默认宽度（桌面端）
    /// </summary>
    public int WindowWidth { get; set; } = 1920;

    /// <summary>
    /// 窗口默认高度（桌面端）
    /// </summary>
    public int WindowHeight { get; set; } = 1080;

    /// <summary>
    /// 设计分辨率宽度（虚拟画布宽度）。所有场景布局基于此分辨率计算，
    /// 再通过 RenderTransform 整体缩放到实际窗口尺寸。默认 1920。
    /// <para>对标 Ren'Py 虚拟分辨率机制——在一个固定画布上布局，GPU 负责缩放绘制。</para>
    /// </summary>
    public int DesignWidth { get; set; } = 1920;

    /// <summary>
    /// 设计分辨率高度（虚拟画布高度）。默认 1080。
    /// </summary>
    public int DesignHeight { get; set; } = 1080;

    /// <summary>
    /// 布局缩放模式——控制设计分辨率到实际窗口的缩放策略。默认 Stretch（填满窗口，不黑边）。
    /// <para>Contain：等比缩放留黑边（对标 Ren'Py 默认）</para>
    /// <para>Cover：等比缩放裁边缘</para>
    /// <para>Stretch：独立 X/Y 缩放填满窗口（轻微变形，VN 游戏推荐）</para>
    /// </summary>
    public LayoutScaleMode ScaleMode { get; set; } = LayoutScaleMode.Stretch;

    /// <summary>
    /// 是否全屏
    /// </summary>
    public bool FullScreen { get; set; }

    /// <summary>
    /// 安全区左偏移（移动端）
    /// </summary>
    public double SafeAreaLeft { get; set; }

    /// <summary>
    /// 安全区上偏移（移动端）
    /// </summary>
    public double SafeAreaTop { get; set; }

    /// <summary>
    /// 安全区右偏移（移动端）
    /// </summary>
    public double SafeAreaRight { get; set; }

    /// <summary>
    /// 安全区下偏移（移动端）
    /// </summary>
    public double SafeAreaBottom { get; set; }

    // ── 调试配置 ──

    /// <summary>
    /// 是否启用性能 HUD（默认 true 开发模式）
    /// </summary>
    public bool ShowPerformanceHud { get; set; } = true;

    /// <summary>
    /// 是否启用热重载（默认 true 开发模式）
    /// </summary>
    public bool EnableHotReload { get; set; } = true;

    // ── 存档配置 ──

    /// <summary>
    /// 游戏版本号（写入 SaveData.GameVersion，用于存档迁移）。默认 "1.0.0"。
    /// </summary>
    public string GameVersion { get; set; } = "1.0.0";

    /// <summary>
    /// 存档名称生成器（输入当前场景名，返回存档显示名称）。
    /// 不提供时默认 "存档 - {场景名}"。
    /// </summary>
    public Func<string, string>? SaveNameFormatter { get; set; }

    /// <summary>
    /// 是否启用游戏时间系统（启用后 __game_time_* 状态会随存档持久化）。默认 false。
    /// <para>关闭时开发者可用普通变量（如 time_of_day="morning"）自行管理时间状态。</para>
    /// <para>启用后进入"小说世界模式"：时间自动推进、时间事件触发、禁用逐句回溯。</para>
    /// </summary>
    public bool EnableTimeSystem { get; set; }

    /// <summary>
    /// 多少现实秒推进 1 游戏分钟。默认 1.0（1 现实秒 = 1 游戏分钟）。
    /// <para>仅在 EnableTimeSystem=true 时生效。</para>
    /// <para>0.5 = 2 倍速（30 现实秒 = 1 游戏天），2.0 = 半速（48 现实分钟 = 1 游戏天）。</para>
    /// <para>默认规则：1 现实秒 = 1 游戏分钟 → 60 秒 = 1 小时 → 24 现实分钟 = 1 游戏天。</para>
    /// </summary>
    public double SecondsPerGameMinute { get; set; } = 1.0;

    /// <summary>
    /// 游戏起始天数（显示值）。默认 1。
    /// <para>仅在 EnableTimeSystem=true 时生效。</para>
    /// <para>CurrentDay = TimeStartDay + (TotalMinutes / 1440)。</para>
    /// <para>设为 1 则从"第 1 天"开始，设为 0 则从"第 0 天"开始。</para>
    /// </summary>
    public int TimeStartDay { get; set; } = 1;

    /// <summary>
    /// 游戏起始小时（0~23）。默认 0。
    /// <para>仅在 EnableTimeSystem=true 时生效。</para>
    /// <para>游戏开始时 TotalMinutes = TimeStartHour * 60 + TimeStartMinute。</para>
    /// </summary>
    public int TimeStartHour { get; set; } = 0;

    /// <summary>
    /// 游戏起始分钟（0~59）。默认 0。
    /// <para>仅在 EnableTimeSystem=true 时生效。</para>
    /// </summary>
    public int TimeStartMinute { get; set; } = 0;

    /// <summary>默认打字机速度（字符/秒），DialogBox 初始化时读取。默认 30。</summary>
    public double DefaultTextSpeed { get; set; } = 30;

    // ── 回溯配置 ──

    /// <summary>
    /// 最大回溯检查点数量（超出时丢弃最旧的）。默认 100。
    /// <para>每个 Say/Menu/Input/Wait 交互点创建一个检查点。</para>
    /// <para>值越大可回退越远，但内存占用越高（每个检查点是全量状态快照）。</para>
    /// </summary>
    public int MaxRollbackCheckpoints { get; set; } = 100;

    /// <summary>
    /// DSL 单帧最大执行命令数（防止无限循环卡死主线程）。默认 200。
    /// <para>Step() 内部 while 循环的上限，达到后让出控制权到下一帧。</para>
    /// </summary>
    public int MaxStepBudget { get; set; } = 200;

    /// <summary>
    /// 进入菜单场景时是否自动清空 Game 堆栈。默认 false（开发者手动控制）。
    /// <para>true 时 Navigate→Menu 自动调 _sceneStack.Clear()。</para>
    /// </summary>
    public bool AutoClearStackOnMenu { get; set; }

    // ── 场景配置 ──

    /// <summary>
    /// 标题/主菜单场景名（默认 "title_main"）。
    /// <para>用于 "返回标题" 导航别名解析和调试日志。</para>
    /// </summary>
    public string TitleSceneName { get; set; } = "title_main";

    /// <summary>
    /// "返回标题" 导航别名（默认 "back_title"）。
    /// <para>当 Navigate 命令的目标等于此值时，自动重定向到 TitleSceneName。</para>
    /// </summary>
    public string BackTitleAlias { get; set; } = "back_title";

    // ── 音频生命周期配置 ──

    /// <summary>
    /// 场景切换时是否自动停止 BGM。默认 true（传统 Galgame 行为）。
    /// <para>每个 PlayBgm 可传 autoStop 参数覆盖此全局配置。</para>
    /// <para>StopBgm 永远可用，不受此配置影响。</para>
    /// </summary>
    public bool DefaultAutoStopBgm { get; set; } = true;

    /// <summary>
    /// 场景切换时是否自动停止语音。默认 true。
    /// <para>每个 PlayVoice 可传 autoStop 参数覆盖。</para>
    /// </summary>
    public bool DefaultAutoStopVoice { get; set; } = true;

    // ── 自适应 ──

    /// <summary>
    /// 图片缓存最大条目数（LRU 淘汰）。默认 128。
    /// <para>超出时自动 Dispose 最久未使用的 Bitmap，防止长时间运行 OOM。</para>
    /// <para>移动端建议 64，桌面端建议 128~256。</para>
    /// </summary>
    public int MaxImageCacheSize { get; set; } = 128;

    // ── 超时配置 ──

    /// <summary>
    /// 阻塞 API 默认超时秒数（SayAsync/WaitForClickAsync 等）。默认 120。
    /// <para>PollUntilTrue 和 TransitionAsync 使用此值。</para>
    /// </summary>
    public int BlockingTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 交互场景超时秒数（ShowMenuAsync/InputAsync/CallScreenAsync）。默认 300。
    /// <para>用户长时间不操作时超时返回，防止永久阻塞。</para>
    /// </summary>
    public int InteractionTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// 过场动画激活等待超时秒数。默认 5。
    /// <para>PlayCutsceneAsync 等待 CutsceneActive 变为 true 的轮询超时。</para>
    /// </summary>
    public int CutsceneActivationTimeoutSeconds { get; set; } = 5;

    // ── 自适应方法 ──

    /// <summary>
    /// 根据当前平台获取目标帧率
    /// </summary>
    public int GetTargetFps()
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            return MobileTargetFps;
        return DesktopTargetFps;
    }

    /// <summary>
    /// 将安全区写入状态容器键
    /// </summary>
    public void WriteSafeAreaToState(IStateContainer state)
    {
        state.Set("safe_left", SafeAreaLeft);
        state.Set("safe_top", SafeAreaTop);
        state.Set("safe_right", SafeAreaRight);
        state.Set("safe_bottom", SafeAreaBottom);
    }
}
