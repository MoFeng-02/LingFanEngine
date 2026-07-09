using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

// ========== 变量操作命令 ==========

/// <summary>
/// 设置变量命令
/// <para>IsDefine=true 表示"仅在变量不存在时设置"，用于 DSL define ... once 语法。</para>
/// </summary>
public readonly record struct SetVariableCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Key { get; init; }
    public object? Value { get; init; }

    /// <summary>
    /// 是否为定义模式（只在键不存在时写入，用于 DSL define 语法）
    /// </summary>
    public bool IsDefine { get; init; }

    public SetVariableCommand() { }
}

// ========== 导航命令 ==========

/// <summary>
/// 路由导航命令
/// </summary>
public readonly record struct NavigateCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public int? SceneIndex { get; init; }

    /// <summary>
    /// 可选：直接指定场景名称（配合 scene "name" 语法）
    /// </summary>
    public string? SceneName { get; init; }

    /// <summary>
    /// 可选：入口标签名。场景从 story 文件懒加载后从此 label 开始执行
    /// </summary>
    public string? EntryLabel { get; init; }

    public NavigateCommand() { }
}

/// <summary>
/// 场景命令——清空 SceneStack 并切换到新场景
/// <para>DSL 中 scene "xxx" 对应此命令，navigate 不改变堆栈。</para>
/// </summary>
public readonly record struct ClearStackCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public ClearStackCommand() { }
}

/// <summary>
/// 重置全部游戏状态命令（返回主菜单时手动调用）
/// <para>清除：非系统变量、局部变量、场景堆栈、回溯检查点、菜单标记、Skip/Auto 模式。</para>
/// <para>保留：__ 前缀的系统偏好（音量、文字速度、已读记录等）。</para>
/// </summary>
public readonly record struct ResetGameStateCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public ResetGameStateCommand() { }
}

/// <summary>
/// 深合并变量定义命令（补缺+修类型）
/// </summary>
public readonly record struct MergeDefinesCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required Dictionary<string, object?> Defines { get; init; }
    public MergeDefinesCommand() { }
}

// ========== 对话命令 ==========

/// <summary>
/// 显示对话命令
/// </summary>
public readonly record struct ShowDialogCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Text { get; init; }
    public string? Speaker { get; init; }

    /// <summary>说话者名字颜色（如 "#FF88FF"）</summary>
    public string? SpeakerColor { get; init; }

    /// <summary>对话文本颜色</summary>
    public string? TextColor { get; init; }

    /// <summary>说话者字体名</summary>
    public string? SpeakerFont { get; init; }

    /// <summary>对话文本字体名</summary>
    public string? TextFont { get; init; }

    /// <summary>打字机效果开关（默认 true）</summary>
    public bool TypewriterEnabled { get; init; } = true;

    /// <summary>对话栏宽度（屏幕百分比，null=全局默认/全宽）</summary>
    public double? DialogPercentW { get; init; }

    /// <summary>对话栏高度（屏幕百分比，null=全局默认/自适应）</summary>
    public double? DialogPercentH { get; init; }

    /// <summary>对话栏左偏移（像素，null=全局默认/0）</summary>
    public double? DialogMarginL { get; init; }

    /// <summary>对话栏底偏移（像素，null=全局默认/0）</summary>
    public double? DialogMarginB { get; init; }

    /// <summary>
    /// 对话期间场景按钮是否可点击（默认 false=模态遮罩激活）
    /// <para>true = say clickable=true / say okey，遮罩隐藏，按钮可交互</para>
    /// </summary>
    public bool Clickable { get; init; }

    /// <summary>侧脸图路径（Phase 24，对标 Ren'Py Character image=）</summary>
    public string? SideImage { get; init; }

    public ShowDialogCommand() { }
}

/// <summary>
/// 追加对话命令（对标 Ren'Py extend）
/// </summary>
public readonly record struct ExtendDialogCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Append { get; init; }
    public ExtendDialogCommand() { }
}

// ========== 音频命令 ==========

/// <summary>
/// 播放 BGM 命令
/// </summary>
public readonly record struct PlayBgmCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public float Volume { get; init; } = 1.0f;

    /// <summary>fadein 渐变持续时间（秒），0 = 即时</summary>
    public double FadeIn { get; init; }

    /// <summary>fadeout 渐变持续时间（秒），0 = 即时</summary>
    public double FadeOut { get; init; }

    /// <summary>场景切换时是否自动停止（null=跟随全局配置）。默认 null。</summary>
    public bool? AutoStop { get; init; }

    public PlayBgmCommand() { }
}

/// <summary>
/// 停止 BGM 命令
/// </summary>
public readonly record struct StopBgmCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public StopBgmCommand() { }
}

/// <summary>
/// 播放音效命令（独立通道，不覆盖 BGM）
/// </summary>
public readonly record struct PlaySeCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public float Volume { get; init; } = 1.0f;
    public PlaySeCommand() { }
}

/// <summary>
/// 播放语音命令（独立通道，不覆盖 BGM/SE）
/// </summary>
public readonly record struct PlayVoiceCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public float Volume { get; init; } = 1.0f;

    /// <summary>场景切换时是否自动停止（null=跟随全局配置）。默认 null。</summary>
    public bool? AutoStop { get; init; }

    public PlayVoiceCommand() { }
}

/// <summary>
/// BGM 交叉淡入队列命令（下一个 BGM 渐出+新 BGM 渐入）
/// </summary>
public readonly record struct BgmQueueCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public required string Path { get; init; }
    public float Volume { get; init; } = 1.0f;
    public double CrossFadeDuration { get; init; } = 2.0;
    public BgmQueueCommand() { }
}

// ========== 视频命令 ==========

/// <summary>
/// 播放视频命令
/// <para>由 VideoManager 处理，写入状态键驱动 SceneView 中的 GpuMediaPlayer 控件。</para>
/// </summary>
public readonly record struct PlayVideoCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>视频文件路径</summary>
    public required string Path { get; init; }

    /// <summary>音量 (0~1)</summary>
    public float Volume { get; init; } = 1.0f;

    /// <summary>是否循环播放</summary>
    public bool Loop { get; init; }

    /// <summary>是否自动播放</summary>
    public bool AutoPlay { get; init; } = true;

    public PlayVideoCommand() { }
}

/// <summary>
/// 停止视频命令
/// </summary>
public readonly record struct StopVideoCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public StopVideoCommand() { }
}

/// <summary>
/// 暂停视频命令
/// </summary>
public readonly record struct PauseVideoCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public PauseVideoCommand() { }
}

/// <summary>
/// 恢复视频播放命令
/// </summary>
public readonly record struct ResumeVideoCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public ResumeVideoCommand() { }
}

/// <summary>
/// 视频跳转命令
/// </summary>
public readonly record struct SeekVideoCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>跳转目标位置（秒）</summary>
    public double Position { get; init; }

    public SeekVideoCommand() { }
}

/// <summary>
/// 全屏过场动画命令
/// <para>对标 Ren'Py renpy.movie_cutscene()——全屏播放、用户可点击跳过、阻塞等待结束。</para>
/// <para>阻塞等待由 GameController.PlayCutsceneAsync 实现（轮询 CutsceneActive 状态键）。</para>
/// </summary>
public readonly record struct CutsceneCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;

    /// <summary>视频文件路径</summary>
    public required string Path { get; init; }

    /// <summary>用户是否可点击跳过</summary>
    public bool Skipable { get; init; } = true;

    /// <summary>音量 (0~1)</summary>
    public float Volume { get; init; } = 1.0f;

    public CutsceneCommand() { }
}

// ========== 等待命令 ==========

/// <summary>
/// 可中断等待命令（对标 Ren'Py pause hard=True）
/// </summary>
public readonly record struct HardPauseCommand : ICommand
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public CommandPriority Priority { get; init; } = CommandPriority.Normal;
    public HardPauseCommand() { }
}
