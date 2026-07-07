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
        /// 阻塞类型 (string)："wait" / "menu" / "input" / "dialog" / ""
        /// <para>联动: <see cref="WaitingType"/></para>
        /// </summary>
        public static class WaitingTypes
        {
            public const string Wait = "wait";
            public const string Menu = "menu";
            public const string Input = "input";
            public const string Dialog = "dialog";
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
        /// <summary>提示消息文本 (string)</summary>
        public const string Text = "__notify_text";
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

        /// <summary>跳过模式是否仅跳过已读文本 (bool)，false=跳过所有</summary>
        public const string SkipOnlySeen = "__skip_only_seen";

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

    // ==================== 回溯（Rollback） ====================

    /// <summary>
    /// Say 级回溯相关状态键
    /// <para>DSL 场景中每个 ShowDialogCommand 创建一个检查点，</para>
    /// <para>支持在对话级别前进/后退（对标 Ren'Py rollback）。</para>
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
    }
}
