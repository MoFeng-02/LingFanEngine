using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.EngineOptions;

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
    /// </summary>
    public bool EnableTimeSystem { get; set; }

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
