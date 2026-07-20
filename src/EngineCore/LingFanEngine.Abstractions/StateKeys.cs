namespace LingFanEngine.Abstractions;

/// <summary>
/// 状态容器键名常量
/// <para>所有以 __ 前缀的系统状态键集中定义于此，提供 IntelliSense 和编译时检查。</para>
/// <para>命名约定：值保持 __ 前缀不变，嵌套类按功能域分组。</para>
/// <para>UI 层（SceneView / DialogBox）可自行引用此类。</para>
/// </summary>
public static class StateKeys
{
    /// <summary>
    /// 系统状态键前缀，所有 __ 开头的键均为引擎内部状态
    /// </summary>
    public const string SystemPrefix = "__";

    /// <summary>系统存档槽名（保存偏好设置）</summary>
    public const string SystemSaveSlot = "__system";
    // ==================== 场景 ====================

    /// <summary>场景相关状态键</summary>
    public static class Scene
    {
        /// <summary>当前场景名 (string)，由 NavigateCommand / SceneCommand 写入</summary>
        public const string CurrentName = "__current_scene_name";

        /// <summary>场景 UI 元素列表 (List&lt;UIElementEntity&gt;)</summary>
        public const string Elements = "__scene_elements";

        /// <summary>场景脏标记 (bool)，SceneView 读取后清除</summary>
        public const string Dirty = "__scene_dirty";

        /// <summary>场景历史堆栈 (List&lt;SceneSnapshot&gt;)，由 SceneStack 管理</summary>
        public const string Stack = "__scene_stack";

        /// <summary>场景前进堆栈 (List&lt;SceneSnapshot&gt;)，由 SceneStack 管理</summary>
        public const string ForwardStack = "__scene_forward_stack";

        /// <summary>运行时元素列表 (List&lt;UIElementEntity&gt;)，show/hide 操作的目标</summary>
        public const string RuntimeElements = "__runtime_elements";

        /// <summary>当前背景路径 (string)，由 background 命令写入</summary>
        public const string CurrentBackground = "__current_background";

        /// <summary>当前语言代码 (string)，如 "zh-CN" / "en-US"</summary>
        public const string CurrentLanguage = "__current_language";

        /// <summary>Menu/UI 场景的来源场景名 (string?)，用于从菜单返回游戏</summary>
        public const string MenuReturnTo = "__menu_return_to";

        /// <summary>当前场景类型 (int, SceneType)，存档/回溯/导航行为依据此值</summary>
        public const string CurrentType = "__current_scene_type";

        /// <summary>
        /// 进入 Menu/UI 前的游戏 DSL 执行位置 (int)
        /// <para>当从 Game 场景导航到 Menu/UI 场景时，DslExecutor.LoadCommands 会重置 CurrentIndex=0。</para>
        /// <para>此键保存进入菜单前的游戏 DSL 位置，供 Menu 场景存档使用。</para>
        /// </summary>
        public const string GameDslIndex = "__game_dsl_index";

        /// <summary>
        /// 进入 Menu/UI 前的游戏 DSL 等待类型 (string)
        /// <para>同 <see cref="GameDslIndex"/>，保存进入菜单前的游戏 DSL 等待状态。</para>
        /// </summary>
        public const string GameDslWaitingType = "__game_dsl_waiting_type";

        /// <summary>
        /// 进入 Menu/UI 前的游戏场景元素列表 (List&lt;UIElementEntity&gt;)
        /// <para>Menu 场景会覆盖 __scene_elements，此键保存游戏的原元素列表供存档使用。</para>
        /// </summary>
        public const string GameSceneElements = "__game_scene_elements";

        /// <summary>
        /// 进入 Menu/UI 前的游戏运行时元素列表 (List&lt;UIElementEntity&gt;)
        /// <para>同 <see cref="GameSceneElements"/>，保存游戏的原运行时元素。</para>
        /// </summary>
        public const string GameRuntimeElements = "__game_runtime_elements";

        /// <summary>
        /// 进入 Menu/UI 前的游戏背景路径 (string)
        /// </summary>
        public const string GameCurrentBackground = "__game_current_background";
    }

    // ==================== 对话 ====================

    /// <summary>对话相关状态键</summary>
    public static class Dialog
    {
        /// <summary>当前对话文本 (string)</summary>
        public const string Text = "__current_dialog_text";

        /// <summary>当前说话者名 (string)</summary>
        public const string Speaker = "__current_dialog_speaker";

        /// <summary>对话是否完成 (bool)，用户点击后置 true</summary>
        public const string Complete = "__dialog_complete";

        /// <summary>是否等待对话完成 (bool)，SayAsync 轮询此键</summary>
        public const string WaitingSayComplete = "__waiting_say_complete";

        /// <summary>说话者名字颜色 (string?)，如 "#FF88FF"</summary>
        public const string SpeakerColor = "__dialog_speaker_color";

        /// <summary>对话文本颜色 (string?)</summary>
        public const string TextColor = "__dialog_text_color";

        /// <summary>说话者字体名 (string?)</summary>
        public const string SpeakerFont = "__dialog_speaker_font";

        /// <summary>对话文本字体名 (string?)</summary>
        public const string TextFont = "__dialog_text_font";

        /// <summary>打字机效果开关 (bool)，默认 true</summary>
        public const string TypewriterEnabled = "__typewriter_enabled";

        /// <summary>对话栏宽度百分比 (double?)，null=全局默认</summary>
        public const string WidthPercent = "__dialog_w_pct";

        /// <summary>对话栏高度百分比 (double?)</summary>
        public const string HeightPercent = "__dialog_h_pct";

        /// <summary>对话栏左偏移像素 (double?)</summary>
        public const string MarginLeft = "__dialog_ml";

        /// <summary>对话栏底偏移像素 (double?)</summary>
        public const string MarginBottom = "__dialog_mb";

        /// <summary>对话栏宽度默认百分比 (double?)，全局默认</summary>
        public const string WidthPercentDefault = "__dialog_w_pct_default";

        /// <summary>对话栏高度默认百分比 (double?)</summary>
        public const string HeightPercentDefault = "__dialog_h_pct_default";

        /// <summary>对话栏默认左偏移像素 (double?)</summary>
        public const string MarginLeftDefault = "__dialog_ml_default";

        /// <summary>对话栏默认底偏移像素 (double?)</summary>
        public const string MarginBottomDefault = "__dialog_mb_default";

        /// <summary>说话者名字默认颜色 (string?)，如 "#FF88FF"</summary>
        public const string SpeakerColorDefault = "__dialog_speaker_color_default";

        /// <summary>对话文本默认颜色 (string?)</summary>
        public const string TextColorDefault = "__dialog_text_color_default";

        /// <summary>打字机是否完成 (bool)，供 Skip/Auto 模式检测</summary>
        public const string TypewriterDone = "__typewriter_done";

        /// <summary>打字机速度 (double, 字符/秒)，null=使用控件默认值</summary>
        public const string TypewriterSpeed = "__typewriter_speed";

        /// <summary>
        /// 对话期间场景按钮是否可点击 (bool)，默认 false（模态遮罩激活）
        /// <para>true = say clickable=true / say okey，遮罩隐藏，按钮可交互</para>
        /// <para>false = 默认模态，透明遮罩拦截点击，仅推进对话</para>
        /// </summary>
public const string Clickable = "__dialog_clickable";

/// <summary>此对话不可跳过 (bool)，Skip 模式下玩家仍需手动点击推进</summary>
public const string Noskip = "__dialog_noskip";

/// <summary>瞬时文本 (bool)，true=跳过打字机效果，立即显示全部文本</summary>
public const string Instant = "__dialog_instant";

/// <summary>对话侧脸图路径 (string?)，由角色定义的 side 属性或 SayAsync 参数设置</summary>
        public const string SideImage = "__dialog_side_image";

        /// <summary>
        /// 对话窗口模式 (string)："auto"=对话时显示无对话隐藏 | "show"=强制显示 | "hide"=强制隐藏
        /// <para>对标 Ren'Py window auto/show/hide</para>
        /// </summary>
        public const string WindowMode = "__dialog_window_mode";

        /// <summary>对话框模板名 (string?)，Phase 65。null=用角色级 screen 或全局默认</summary>
        public const string Template = "__dialog_template";
    }

    // ==================== 过渡动画 ====================

    /// <summary>过渡动画相关状态键</summary>
    public static class Transition
    {
        /// <summary>过渡是否活跃 (bool)</summary>
        public const string Active = "__transition_active";

        /// <summary>过渡类型 (string)，如 "FadeIn" / "SlideLeftIn" / "ZoomIn"</summary>
        public const string Type = "__transition_type";

        /// <summary>过渡进度 0~1 (double)</summary>
        public const string Progress = "__transition_progress";

        /// <summary>过渡时长秒 (double)</summary>
        public const string Duration = "__transition_duration";

        /// <summary>过渡已用时间秒 (double)</summary>
        public const string Elapsed = "__transition_elapsed";

        /// <summary>水平偏移量 (double)，用于 Slide 过渡</summary>
        public const string OffsetX = "__transition_offset_x";

        /// <summary>垂直偏移量 (double)，用于 Slide 过渡</summary>
        public const string OffsetY = "__transition_offset_y";

        /// <summary>缩放比例 (double)，用于 Zoom 过渡</summary>
        public const string Scale = "__transition_scale";

        /// <summary>缓动函数名 (string)，如 "EaseOutQuad"</summary>
        public const string Easing = "__transition_easing";
    }

    // ==================== DSL 执行器 ====================

    /// <summary>DSL 执行器相关状态键</summary>
    public static class Dsl
    {
        /// <summary>当前命令列表 (List&lt;ICommand&gt;)</summary>
        public const string Commands = "__dsl_commands";

        /// <summary>当前 label 索引 (Dictionary&lt;string, int&gt;)</summary>
        public const string Labels = "__dsl_labels";

        /// <summary>当前执行位置 (int)</summary>
        public const string CurrentIndex = "__dsl_current_index";

        /// <summary>是否正在执行 (bool)</summary>
        public const string Executing = "__dsl_executing";

        /// <summary>阻塞类型 (string)："wait" / "menu" / "input" / "dialog" / ""</summary>
        public const string WaitingType = "__dsl_waiting_type";

        /// <summary>阻塞附加值 (object?)</summary>
        public const string WaitingValue = "__dsl_waiting_value";

        /// <summary>命令总数 (int)</summary>
        public const string TotalCommands = "__dsl_total_commands";

        /// <summary>wait 阻塞的截止时间戳 (double)，Environment.TickCount64/1000.0</summary>
        public const string WaitUntil = "__dsl_wait_until";

        /// <summary>wait 阻塞的等待时长 (double)</summary>
        public const string WaitDuration = "__dsl_wait_duration";

        /// <summary>通用等待标记 (bool)</summary>
        public const string Waiting = "__dsl_waiting";

        /// <summary>跳转目标 label (string)，仅记录用</summary>
        public const string JumpTarget = "__dsl_jump_target";

        /// <summary>跳转目标索引 (int)，仅记录用</summary>
        public const string JumpIndex = "__dsl_jump_index";

        /// <summary>
        /// C# 场景回放代次 (int)——每次 RestoreAndRestart 递增
        /// <para>用于取消过期的 C# 场景 Runner：SayAsync 等阻塞方法检测代次变化后提前返回。</para>
        /// </para>排除出回溯快照（s_rollbackKeys），不随检查点保存/恢复。</para>
        /// </summary>
        public const string CSharpReplayGeneration = "__csharp_replay_generation";

        /// <summary>
        /// 阻塞类型 (string)："wait" / "menu" / "input" / "dialog" / ""
        /// <para>联动: <see cref="WaitingType"/></para>
        /// </summary>
        public static class WaitingTypes
        {
public const string Wait = "wait";
public const string WaitSkipable = "wait_skipable";
public const string Menu = "menu";
public const string Input = "input";
public const string Dialog = "dialog";
public const string Pause = "pause";
public const string CallScreen = "call_screen";
        }
    }

    // ==================== 菜单 ====================

    /// <summary>菜单相关状态键</summary>
    public static class Menu
    {
        /// <summary>菜单提示文本 (string)</summary>
        public const string Prompt = "__menu_prompt";

        /// <summary>菜单选项数组 (string[])</summary>
        public const string Options = "__menu_options";

        /// <summary>选中索引 (int)，-1=未选择</summary>
        public const string Selected = "__menu_selected";

        /// <summary>DSL 菜单提示文本 (string)</summary>
        public const string DslPrompt = "__dsl_menu_prompt";

        /// <summary>DSL 菜单选项 (string)，以 | 分隔</summary>
        public const string DslOptions = "__dsl_menu_options";

        /// <summary>DSL 菜单目标 label (string)，以 , 分隔</summary>
        public const string DslTargets = "__dsl_menu_targets";

        /// <summary>DSL 菜单文本 (string)，以 , 分隔</summary>
        public const string DslTexts = "__dsl_menu_texts";
    }

    // ==================== 输入 ====================

    /// <summary>输入相关状态键</summary>
    public static class Input
    {
        /// <summary>输入提示文本 (string)</summary>
        public const string Prompt = "__input_prompt";

        /// <summary>输入结果 (string?)，null=未提交</summary>
        public const string Result = "__input_result";

        /// <summary>输入选项 (string[])</summary>
        public const string Options = "__input_options";

        /// <summary>DSL 输入提示文本 (string)</summary>
        public const string DslPrompt = "__dsl_input_prompt";

        /// <summary>DSL 输入存储键名 (string)</summary>
        public const string DslStore = "__dsl_input_store";

        /// <summary>DSL 输入选项 (string)，以 , 分隔</summary>
        public const string DslOptions = "__dsl_input_options";

        /// <summary>DSL 输入等待标记 (bool)</summary>
        public const string DslWaiting = "__dsl_input_waiting";

        /// <summary>最近一次输入事件 (InputEvent)，由 InputService 写入</summary>
        public const string LastEvent = "__input_last";
    }

    // ==================== 音频 ====================

    /// <summary>音频相关状态键</summary>
    public static class Audio
    {
        /// <summary>当前 BGM 路径 (string)</summary>
        public const string CurrentBgmPath = "__current_bgm_path";

        /// <summary>当前 BGM 音量 (float)</summary>
        public const string CurrentBgmVolume = "__current_bgm_volume";

        /// <summary>BGM 场景切换时自动停止 (bool?)</summary>
        public const string BgmAutoStop = "__bgm_auto_stop";

        /// <summary>Voice 场景切换时自动停止 (bool?)</summary>
        public const string VoiceAutoStop = "__voice_auto_stop";

/// <summary>BGM 路径 (string)，AudioManager 内部用</summary>
public const string BgmPath = "__bgm_path";

/// <summary>当前环境音路径 (string)</summary>
public const string AmbientPath = "__ambient_path";
}

    // ==================== 视频 ====================

    /// <summary>视频相关状态键</summary>
    public static class Video
    {
        /// <summary>当前视频文件路径 (string)</summary>
        public const string CurrentPath = "__video_current_path";

        /// <summary>视频是否正在播放 (bool)</summary>
        public const string IsPlaying = "__video_is_playing";

        /// <summary>视频是否暂停 (bool)</summary>
        public const string IsPaused = "__video_is_paused";

        /// <summary>视频音量 (float, 0~1)</summary>
        public const string Volume = "__video_volume";

        /// <summary>是否循环播放 (bool)</summary>
        public const string Loop = "__video_loop";

        /// <summary>是否自动播放 (bool)</summary>
        public const string AutoPlay = "__video_autoplay";

        /// <summary>跳转目标位置（秒）(double?)，SceneView 读取后清空</summary>
        public const string SeekPosition = "__video_seek_position";

        /// <summary>视频总时长（秒）(double)，由 SceneView 回写</summary>
        public const string Duration = "__video_duration";

        /// <summary>视频当前播放位置（秒）(double)，由 SceneView 回写</summary>
        public const string Position = "__video_position";

        /// <summary>视频是否已播放结束 (bool)，由 SceneView 回写</summary>
        public const string IsFinished = "__video_is_finished";

        /// <summary>是否处于全屏过场动画模式 (bool)</summary>
        public const string CutsceneActive = "__video_cutscene_active";

        /// <summary>过场动画是否被用户跳过 (bool)</summary>
        public const string CutsceneSkipped = "__video_cutscene_skipped";

        /// <summary>过场动画是否允许用户跳过 (bool)</summary>
        public const string CutsceneSkipable = "__video_cutscene_skipable";
    }

    // ==================== 游戏时间 ====================

    /// <summary>游戏时间相关状态键</summary>
    public static class GameTime
    {
        /// <summary>游戏时间键前缀，用于扫描所有游戏时间相关键</summary>
        public const string Prefix = "__game_time_";

        /// <summary>游戏累计时间分钟 (long)，1 现实秒 = 1 游戏分钟</summary>
        public const string TotalMinutes = "__game_time_total_minutes";

        /// <summary>时间系统暂停 (bool)</summary>
        public const string Paused = "__game_time_paused";

        /// <summary>时间缩放系数 (float)，1.0 = 正常</summary>
        public const string Scale = "__game_time_scale";

        /// <summary>当前游戏天数 (int)——由 EventScheduler 在 OnTimeAdvanced 时更新，供条件表达式引用</summary>
        public const string CurrentDay = "__current_day";
    }

    // ==================== 控件级动画 ====================

    /// <summary>
    /// 控件级动画相关状态键
    /// <para>动画键格式：__anim_{target}_{property}_{suffix}</para>
    /// <para>suffix: from / target / duration / easing / elapsed / active / current / repeat</para>
    /// <para>此前的动态键使用前缀拼接，不定义为常量。</para>
    /// </summary>
    public static class Animation
    {
        /// <summary>动画键前缀，用于扫描所有活跃动画</summary>
        public const string Prefix = "__anim_";

        /// <summary>动画活跃后缀</summary>
        public const string ActiveSuffix = "_active";

        /// <summary>动画起始值后缀</summary>
        public const string FromSuffix = "_from";

        /// <summary>动画目标值后缀</summary>
        public const string TargetSuffix = "_target";

        /// <summary>动画时长后缀</summary>
        public const string DurationSuffix = "_duration";

        /// <summary>动画缓动后缀</summary>
        public const string EasingSuffix = "_easing";

        /// <summary>动画已用时间后缀</summary>
        public const string ElapsedSuffix = "_elapsed";

        /// <summary>动画当前值后缀</summary>
        public const string CurrentSuffix = "_current";

        /// <summary>动画剩余重复次数后缀</summary>
        public const string RepeatSuffix = "_repeat";
    }

    // ==================== 通知 ====================

    /// <summary>通知/提示相关状态键</summary>
    public static class Notify
    {
        /// <summary>提示消息文本 (string)，设为非 null 时触发 Toast 显示</summary>
        public const string Text = "__notify_text";

        /// <summary>通知类型 (string)："info" / "warning" / "error"，默认 "info"</summary>
        public const string Type = "__notify_type";

        /// <summary>通知队列 (List&lt;NotificationItem&gt;)，支持排队显示多条通知</summary>
        public const string Queue = "__notify_queue";

        /// <summary>当前通知的显示时长秒数 (double)，由 NotifyHandler 写入</summary>
        public const string Duration = "__notify_duration";
    }

    // ==================== call/return 调用栈 ====================

    /// <summary>call/return 调用栈相关状态键</summary>
    public static class CallStack
    {
        /// <summary>call 栈 (List&lt;int&gt;)，存储返回位置索引</summary>
        public const string Stack = "__call_stack";
    }

    // ==================== 故事加载 ====================

    /// <summary>故事加载相关状态键</summary>
    public static class Story
    {
        /// <summary>当前故事语言 (string)</summary>
        public const string Lang = "__story_lang";

        /// <summary>当前故事文件路径 (string?)，由 NavigateCommand 写入</summary>
        public const string CurrentPath = "__current_path";

        /// <summary>故事加载错误键前缀，完整键为 __story_error_{storyId}</summary>
        public const string ErrorPrefix = "__story_error_";

        /// <summary>故事加载完成标记键前缀，完整键为 __story_loaded_{storyId}</summary>
        public const string LoadedPrefix = "__story_loaded_";
    }

    // ==================== UI 标签 ====================

    /// <summary>
    /// UI 层内部使用的控件 Tag 值和属性键
    /// <para>这些不是状态容器键，而是 Avalonia 控件 Tag / 属性字典键，
    /// 集中定义以避免魔法字符。</para>
    /// </summary>
    public static class UiTags
    {
        /// <summary>UIElementEntity.Properties 中的标签键，用于动画匹配和 show/hide 定位</summary>
        public const string Tag = "__tag";

        /// <summary>运行时动态添加控件的 Tag 值（show/hide 命令产生的元素）</summary>
        public const string Runtime = "__runtime";

        /// <summary>通知 Toast 控件的 Tag 值</summary>
        public const string Notify = "__notify";
    }

    // ==================== 杂项 ====================

    /// <summary>DSL fallback 命令名 (string)，用于 CommandService 未匹配命令的回退处理</summary>
    public const string DslFallback = "__dsl_fallback";

    /// <summary>硬暂停信号 (string?)，用于 HardPauseCommand</summary>
    public const string HardPauseSignal = "__hard_pause_signal";

    /// <summary>按钮命令值 (string)，按钮点击时写入，供 CommandService 处理</summary>
    public const string ButtonCommand = "__button_command";

    // ==================== 对话历史 ====================

    /// <summary>对话历史相关状态键</summary>
    public static class History
    {
        /// <summary>对话历史列表 (List&lt;DialogHistoryEntry&gt;)</summary>
        public const string Entries = "__dialog_history";

        /// <summary>对话历史最大条数 (int)，默认 100</summary>
        public const string MaxCount = "__dialog_history_max";

        /// <summary>对话历史是否可见 (bool)，UI 层读取此键显示/隐藏历史面板</summary>
        public const string Visible = "__dialog_history_visible";
    }

    // ==================== 跳过/自动模式 ====================

    /// <summary>跳过/自动模式相关状态键</summary>
    public static class Playback
    {
        /// <summary>跳过模式是否激活 (bool)</summary>
        public const string SkipActive = "__skip_active";

        /// <summary>自动模式是否激活 (bool)</summary>
        public const string AutoActive = "__auto_active";

        /// <summary>自动模式延迟秒数 (double)，对话完成后等待多久自动推进</summary>
        public const string AutoDelay = "__auto_delay";

        /// <summary>自动模式当前计时器 (double)，内部用</summary>
        public const string AutoTimer = "__auto_timer";

/// <summary>已读 Say 的场景感知键集合 (HashSet&lt;string&gt;，格式 "sceneName:cmdIdx")，用于 Skip 仅跳已读</summary>
public const string SeenSayIndices = "__seen_say_indices";
    }

    // ==================== 偏好设置 ====================

    /// <summary>偏好设置相关状态键</summary>
    public static class Preferences
    {
        /// <summary>主音量 (float, 0~1)</summary>
        public const string MasterVolume = "__pref_master_volume";

        /// <summary>BGM 音量 (float, 0~1)</summary>
        public const string BgmVolume = "__pref_bgm_volume";

        /// <summary>SE 音量 (float, 0~1)</summary>
        public const string SeVolume = "__pref_se_volume";

        /// <summary>语音音量 (float, 0~1)</summary>
        public const string VoiceVolume = "__pref_voice_volume";

        /// <summary>主静音 (bool)</summary>
        public const string MasterMuted = "__pref_master_muted";

        /// <summary>打字机速度 (double, 字符/秒)</summary>
        public const string TextSpeed = "__pref_text_speed";

        /// <summary>自动模式延迟 (double, 秒)</summary>
        public const string AutoForwardDelay = "__pref_auto_delay";

        /// <summary>跳过未读文本 (bool)</summary>
        public const string SkipUnseen = "__pref_skip_unseen";

        /// <summary>全屏模式 (bool)</summary>
        public const string Fullscreen = "__pref_fullscreen";

        /// <summary>语言 (string)</summary>
        public const string Language = "__pref_language";
    }

    // ==================== 屏幕震动 ====================

    /// <summary>屏幕震动相关状态键</summary>
    public static class Shake
    {
        /// <summary>震动是否活跃 (bool)</summary>
        public const string Active = "__shake_active";

        /// <summary>震动强度 (double, 像素)</summary>
        public const string Intensity = "__shake_intensity";

        /// <summary>震动持续时间 (double, 秒)</summary>
        public const string Duration = "__shake_duration";

        /// <summary>震动已用时间 (double, 秒)</summary>
        public const string Elapsed = "__shake_elapsed";

        /// <summary>当前 X 偏移 (double, 像素)</summary>
        public const string OffsetX = "__shake_offset_x";

        /// <summary>当前 Y 偏移 (double, 像素)</summary>
        public const string OffsetY = "__shake_offset_y";
    }

    // ==================== CG鉴赏/回想 ====================

    /// <summary>CG鉴赏/回想模式相关状态键</summary>
    public static class Gallery
    {
        /// <summary>已解锁 CG 列表 (List&lt;GalleryEntry&gt;)</summary>
        public const string Unlocked = "__gallery_unlocked";

        /// <summary>鉴赏面板是否可见 (bool)</summary>
        public const string Visible = "__gallery_visible";
    }

    // ==================== 成就系统 ====================

    /// <summary>成就系统状态键</summary>
    public static class Achievements
    {
        /// <summary>已解锁成就列表 (List&lt;AchievementEntry&gt;)</summary>
        public const string Unlocked = "__achievements_unlocked";
    }

    // ==================== 章节系统 ====================

    /// <summary>章节系统状态键</summary>
    public static class Chapters
    {
        /// <summary>已解锁章节列表 (List&lt;ChapterEntry&gt;)</summary>
        public const string Unlocked = "__chapters_unlocked";
    }

    // ==================== 播放控制增强 ====================

    /// <summary>播放控制增强状态键</summary>
    public static class PlaybackControl
    {
        /// <summary>是否禁止跳过 (bool)</summary>
        public const string NoSkip = "__no_skip";

        /// <summary>是否强制跳过 (bool)</summary>
        public const string ForceSkip = "__force_skip";

        /// <summary>自动存档开关 (bool)</summary>
        public const string AutoSave = "__auto_save";

        /// <summary>视频结束后自动导航的场景名 (string?)</summary>
        public const string VideoAutoNav = "__video_auto_nav";
    }

    // ==================== Live2D ====================

    /// <summary>Live2D 相关状态键</summary>
    public static class Live2D
    {
        /// <summary>模型定义键前缀（__live2d_char_ + 模型 ID）</summary>
        public const string CharPrefix = "__live2d_char_";

        /// <summary>活跃模型状态键前缀（__live2d_ + 模型 ID + _state）</summary>
        public const string ModelPrefix = "__live2d_";

        /// <summary>动作状态键后缀</summary>
        public const string MotionSuffix = "_motion";

        /// <summary>表情状态键后缀</summary>
        public const string ExprSuffix = "_expr";

        /// <summary>参数状态键前缀（__live2d_ + 模型 ID + _param_ + 参数名）</summary>
        public const string ParamPrefix = "_param_";

        /// <summary>暂停状态键后缀</summary>
        public const string PausedSuffix = "_paused";
    }

    // ==================== 调试控制台 ====================

    /// <summary>调试控制台相关状态键</summary>
    public static class Debug
    {
        /// <summary>调试日志列表 (List&lt;DebugLogEntry&gt;)</summary>
        public const string Logs = "__debug_logs";

        /// <summary>调试控制台是否可见 (bool)</summary>
        public const string Visible = "__debug_visible";

        /// <summary>调试模式是否开启 (bool)</summary>
        public const string Enabled = "__debug_enabled";

        /// <summary>日志最大条数 (int)，默认 500</summary>
        public const string MaxLogs = "__debug_max_logs";
    }

    // ==================== NVL 模式 ====================

    /// <summary>NVL 模式相关状态键</summary>
    public static class Nvl
    {
        /// <summary>NVL 模式是否激活 (bool)</summary>
        public const string Active = "__nvl_active";

        /// <summary>NVL 累积文本 (string)</summary>
        public const string Text = "__nvl_text";

        /// <summary>NVL 累积说话者列表 (string，以 \n 分隔)</summary>
        public const string Speakers = "__nvl_speakers";

        /// <summary>NVL 已积累的条目数 (int)</summary>
        public const string Count = "__nvl_count";
    }

    // ==================== 角色定义 ====================

    /// <summary>
    /// 角色定义状态键
    /// <para>character "boss" name="魔王" color="#FF4444" → state["__char_boss"] = {"name":"魔王","color":"#FF4444"}</para>
    /// </summary>
    public static class Characters
    {
        /// <summary>角色定义键前缀（__char_ + speaker 名）</summary>
        public const string Prefix = "__char_";
    }

    // ==================== 样式表 ====================

    /// <summary>
    /// 样式表状态键
    /// <para>style "btn_primary" color=#88CCFF → state["__style_btn_primary"] = {"color":"#88CCFF"}</para>
    /// <para>元素通过 class="btn_primary" 引用样式，样式属性作为默认值，元素自身属性覆盖。</para>
    /// </summary>
    public static class Styles
    {
        /// <summary>样式定义键前缀（__style_ + 样式名）</summary>
        public const string Prefix = "__style_";
    }

    /// <summary>call_screen 返回结果 (string?)，UI 场景设置此键通知 DslExecutor 继续</summary>
/// <summary>
/// Screen 相关状态键
/// </summary>
public static class Screen
{
/// <summary>call_screen 返回结果 (string?)，UI 场景通过 SetScreenResult 设置</summary>
public const string Result = "__screen_result";

/// <summary>call_screen 传入的参数字典 (Dictionary&lt;string, object?&gt;?)，UI 场景可读取</summary>
public const string Params = "__screen_params";

/// <summary>当前显示的场景名 (string)，由 SceneView/Gameloop 写入</summary>
public const string ActiveScreen = "__active_screen";
}

// 向后兼容：保留旧引用
/// <summary>call_screen 返回结果 (string?)，UI 场景通过 SetScreenResult 设置</summary>
public const string ScreenResult = Screen.Result;

    // ==================== 性能监控 ====================

    /// <summary>性能监控相关状态键</summary>
    public static class Performance
    {
        /// <summary>FPS (double)</summary>
        public const string Fps = "__perf_fps";

        /// <summary>帧时间毫秒 (double)</summary>
        public const string FrameTimeMs = "__perf_frame_ms";

        /// <summary>命令管道队列深度 (int)</summary>
        public const string CommandQueueDepth = "__perf_cmd_queue";

        /// <summary>DSL 当前执行索引 (int)</summary>
        public const string DslCurrentIndex = "__perf_dsl_index";

        /// <summary>DSL 命令总数 (int)</summary>
        public const string DslTotalCommands = "__perf_dsl_total";

        /// <summary>活跃动画数量 (int)</summary>
        public const string ActiveAnimations = "__perf_animations";

        /// <summary>场景元素数量 (int)</summary>
        public const string SceneElementCount = "__perf_scene_elements";

        /// <summary>托管内存 MB (double)</summary>
        public const string MemoryMb = "__perf_memory_mb";

        /// <summary>回溯检查点数量 (int)</summary>
        public const string CheckpointCount = "__perf_checkpoints";

        /// <summary>是否显示性能 HUD (bool)</summary>
        public const string ShowHud = "__perf_show_hud";
    }

    // ==================== 回溯（Rollback） ====================

    /// <summary>
    /// 统一线性回溯时间线相关状态键（Phase 16/16.1）
    /// <para>检查点列表 + CurrentIndex 前沿模型，支持跨场景回溯。</para>
    /// <para>say/menu/input/wait/scene_idle/navigate 创建 RollbackCheckpoint（全量状态快照）。</para>
    /// <para>新交互截断未来检查点，IsReplay 控制回溯重展示不记录历史。</para>
    /// </summary>
    public static class Rollback
    {
        /// <summary>检查点列表 (List&lt;RollbackCheckpoint&gt;)</summary>
        public const string Checkpoints = "__rollback_checkpoints";

        /// <summary>当前检查点位置 (int)，-1 = 尚未创建任何检查点</summary>
        public const string CurrentIndex = "__rollback_current_index";

        /// <summary>是否正在浏览历史 (bool)，true = 用户回退到了历史中某点</summary>
        public const string IsActive = "__rollback_is_active";

        /// <summary>是否为回溯重展示（bool），true = 回退/前进重新展示 Say，不应记录历史</summary>
        public const string IsReplay = "__rollback_is_replay";

        /// <summary>
        /// 回溯阻止标记 (int)：命令索引，>=此索引的检查点不创建
        /// <para>对标 Ren'Py renpy.block_rollback()——block_rollback 设置此键为当前命令索引，
        /// 后续 CreateCheckpoint 检查并跳过。fix_rollback 清除此键。</para>
        /// </summary>
        public const string BlockedUntil = "__rollback_blocked_until";
    }
}
