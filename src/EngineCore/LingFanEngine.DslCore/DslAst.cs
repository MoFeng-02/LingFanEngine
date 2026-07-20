//using LingFanEngine.Abstractions.Entities.UIs;

using LingFanEngine.Abstractions.Entities.UIs;
using LingFanEngine.Abstractions.Interfaces.Events;

namespace LingFanEngine.DslCore;

/// <summary>
/// DSL 语句 AST 抽象基类
/// </summary>
public abstract partial class DslStatement
{
    /// <summary>原始行号（0-based）</summary>
    public int LineNumber { get; set; }
}

// ====== 控制流 ======

public sealed partial class LabelStmt : DslStatement
{
    public required string Name { get; init; }
}

public sealed partial class JumpStmt : DslStatement
{
    public required string TargetLabel { get; init; }
}

public sealed partial class CallStmt : DslStatement
{
    public required string TargetLabel { get; init; }
}

public sealed partial class ReturnStmt : DslStatement
{
    /// <summary>返回值表达式（可选，null=无返回值）</summary>
    public string? ValuePart { get; init; }
}

// ====== 对话与导航 ======

public sealed partial class SayStmt : DslStatement
{
    public required string Text { get; init; }
    public string? Speaker { get; init; }
    /// <summary>对话期间场景按钮是否可点击（say clickable=true / say okey）</summary>
    public bool Clickable { get; init; }
    /// <summary>此对话不可跳过（say noskip=true）</summary>
    public bool Noskip { get; init; }
    /// <summary>瞬时显示文本（say instant=true，跳过打字机效果）</summary>
    public bool Instant { get; init; }
    /// <summary>显式启用打字机效果（say typewriter=true）</summary>
    public bool? Typewriter { get; init; }
    /// <summary>对话框模板名（say template="xxx"，Phase 65）。null=用角色级 screen 或全局默认</summary>
    public string? Template { get; init; }
    /// <summary>行内语音路径（say "text" voice="path"）。非空时对话展示同时播放语音。</summary>
    public string? VoicePath { get; init; }
}

public sealed partial class NavigateStmt : DslStatement
{
    public required string Path { get; init; }
    public string? SceneName { get; init; }
}

public sealed partial class SceneStmt : DslStatement
{
    public required string SceneName { get; init; }
}

public sealed partial class BackStmt : DslStatement { }

public sealed partial class ForwardStmt : DslStatement { }

// ====== 变量操作 ======

public sealed partial class SetStmt : DslStatement
{
    public required string Key { get; init; }
    public required string ValuePart { get; init; }
}

public sealed partial class DefineStmt : DslStatement
{
    public required string Key { get; init; }
    public required string ValuePart { get; init; }
}

public sealed partial class LetStmt : DslStatement
{
    public required string Key { get; init; }
    public required string ValuePart { get; init; }
}

/// <summary>
/// undef 销毁变量语句——DSL 2.0
/// <para>语法：undef "key"</para>
/// <para>编译为 SetVariableCommand { Value = null }，同时清理 _local_ 前缀的局部变量。</para>
/// </summary>
public sealed partial class UndefStmt : DslStatement
{
    public required string Key { get; init; }
}

// ====== 媒体 ======

public sealed partial class BgmStmt : DslStatement
{
    public required string Path { get; init; }
    public float? Volume { get; init; }
}

/// <summary>
/// 音效语句——DSL 2.0
/// <para>语法：se "path" [volume=N]</para>
/// <para>一次性播放，不循环。</para>
/// </summary>
public sealed partial class SeStmt : DslStatement
{
    public required string Path { get; init; }
    public float? Volume { get; init; }
}

/// <summary>
/// 环境音语句——DSL 2.0
/// <para>语法：ambient "path" [loop=true] [volume=N]</para>
/// <para>循环播放的环境音，独立于 BGM/SE/Voice 通道。</para>
/// </summary>
public sealed partial class AmbientStmt : DslStatement
{
    public required string Path { get; init; }
    public bool Loop { get; init; } = true;
    public float? Volume { get; init; }
}

/// <summary>
/// 语音语句——对标 Ren'Py 的 voice 命令
/// <para>语法：voice "path" [volume=N] [auto_stop=true|false]</para>
/// <para>独立语音通道（单轨，原子替换），不覆盖 BGM/SE/Ambient。默认随前进自动停止（DefaultAutoStopVoice）。</para>
/// </summary>
public sealed partial class VoiceStmt : DslStatement
{
    public required string Path { get; init; }
    public float? Volume { get; init; }
    /// <summary>前进时是否自动停止（null=跟随全局配置 DefaultAutoStopVoice）。默认 null。</summary>
    public bool? AutoStop { get; init; }
}

/// <summary>
/// 停止环境音语句——DSL 2.0
/// <para>语法：stop_ambient</para>
/// </summary>
public sealed partial class StopAmbientStmt : DslStatement { }

/// <summary>
/// 停止语音语句
/// <para>语法：stop_voice</para>
/// <para>立即停止当前语音通道（单轨）。用于 say 后无下一句时主动中断，避免语音不会自停。</para>
/// </summary>
public sealed partial class StopVoiceStmt : DslStatement { }

// ====== 视频 ======

public sealed partial class VideoStmt : DslStatement
{
    public required string Path { get; init; }
    public float? Volume { get; init; }
    public bool Loop { get; init; }
    public bool AutoPlay { get; init; } = true;
}

public sealed partial class StopVideoStmt : DslStatement { }

public sealed partial class PauseVideoStmt : DslStatement { }

public sealed partial class ResumeVideoStmt : DslStatement { }

public sealed partial class SeekVideoStmt : DslStatement
{
    public double Position { get; init; }
}

public sealed partial class CutsceneStmt : DslStatement
{
    public required string Path { get; init; }
    public bool Skipable { get; init; } = true;
    public float? Volume { get; init; }
}

public sealed partial class BackgroundStmt : DslStatement
{
    public required string Path { get; init; }
}

public sealed partial class ShowStmt : DslStatement
{
    public required string Target { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    /// <summary>过渡效果名称（如 fade, dissolve）—Phase 25</summary>
    public string? Transition { get; init; }
    /// <summary>过渡持续时间（秒）—Phase 25</summary>
    public double? TransitionDuration { get; init; }
}

public sealed partial class HideStmt : DslStatement
{
    public required string Target { get; init; }
    /// <summary>过渡效果名称（如 fade, dissolve）—Phase 25</summary>
    public string? Transition { get; init; }
    /// <summary>过渡持续时间（秒）—Phase 25</summary>
    public double? TransitionDuration { get; init; }
}

public sealed partial class AnimateStmt : DslStatement
{
    public required string Target { get; init; }
    public required string Property { get; init; }
    public required double TargetValue { get; init; }
    public double? Duration { get; init; }
    public string? Easing { get; init; }
}

// ====== 流程控制 ======

public sealed partial class WaitStmt : DslStatement
{
    public required double Seconds { get; init; }
    /// <summary>是否可跳过——true 时用户点击可提前结束</summary>
    public bool IsSkipable { get; init; }
}

/// <summary>
/// pause 语句——对标 Ren'Py pause
/// <para>pause          → 等待用户点击（Seconds=null）</para>
/// <para>pause N        → 可跳过的定时等待（IsHard=false）</para>
/// <para>pause N hard   → 不可跳过的定时等待（IsHard=true，等同 wait N）</para>
/// </summary>
public sealed partial class PauseStmt : DslStatement
{
    /// <summary>等待秒数，null=等待点击</summary>
    public double? Seconds { get; init; }
    /// <summary>是否不可跳过（hard=true）</summary>
    public bool IsHard { get; init; }
}

public sealed partial class TransitionStmt : DslStatement
{
    public required string Type { get; init; }
    public double? Duration { get; init; }
}

public sealed partial class InputStmt : DslStatement
{
    public required string Prompt { get; init; }
    public required string StoreKey { get; init; }
    public string[]? Options { get; init; }
}

public sealed partial class MenuStmt : DslStatement
{
    public required string Prompt { get; init; }
}

/// <summary>
/// 菜单选项行："选项文本" -> target_label
/// </summary>
public sealed partial class MenuOptionStmt : DslStatement
{
    public required string Text { get; init; }
    public required string TargetLabel { get; init; }
}

public sealed partial class SaveStmt : DslStatement
{
    public required string SlotId { get; init; }
    /// <summary>存档标题（可选，DSL 2.0：save "slot" title "标题"）</summary>
    public string? Title { get; init; }
    /// <summary>是否截取截图（可选，DSL 2.0：screenshot=true）</summary>
    public bool Screenshot { get; init; } = true;
}

public sealed partial class LoadStmt : DslStatement
{
    public required string SlotId { get; init; }
}

// ====== 条件/循环块结构（缩进式，无 end 关键字）======

public sealed partial class IfStmt : DslStatement
{
    public required string Condition { get; init; }
}

public sealed partial class ElseIfStmt : DslStatement
{
    public required string Condition { get; init; }
}

public sealed partial class ElseStmt : DslStatement { }

public sealed partial class WhileStmt : DslStatement
{
    public required string Condition { get; init; }
}

/// <summary>
/// 已废弃：end 关键字不再用于块结束或 label 终止。
/// 保留解析兼容性，编译时作为 no-op 跳过。
/// </summary>
public sealed partial class EndStmt : DslStatement { }

/// <summary>
/// break 语句——退出当前循环（while/for）
/// </summary>
public sealed partial class BreakStmt : DslStatement { }

/// <summary>
/// continue 语句——跳过当前迭代，进入下一次循环
/// </summary>
public sealed partial class ContinueStmt : DslStatement { }

// ====== 额外命令 ======

public sealed partial class ShakeStmt : DslStatement
{
    public double? Intensity { get; init; }
    public double? Duration { get; init; }
}

public sealed partial class ToggleSkipStmt : DslStatement { }

public sealed partial class ToggleAutoStmt : DslStatement { }

public sealed partial class GalleryUnlockStmt : DslStatement
{
    public required string Id { get; init; }
    /// <summary>CG 图片路径（可选——gallery_unlock 语法不需要路径）</summary>
    public string ImagePath { get; init; } = "";
    public string? Title { get; init; }
    public string? SceneName { get; init; }
}

public sealed partial class DebugLogStmt : DslStatement
{
    public required string Message { get; init; }
    public string? Level { get; init; }
}

public sealed partial class NvlStmt : DslStatement
{
    /// <summary>清空累积文本（不退出 NVL 模式）</summary>
    public bool IsClear { get; init; }

    /// <summary>退出 NVL 模式并清空累积文本（恢复 ADV 模式）</summary>
    public bool IsExit { get; init; }
}

/// <summary>
/// 角色定义语句——定义角色对话样式（颜色/字体/名字等）
/// <para>语法：character "speaker_key" name="显示名" color="#FF4444" font="Microsoft YaHei" ...</para>
/// </summary>
public sealed partial class CharacterStmt : DslStatement
{
    /// <summary>角色标识符（say 的 speaker 匹配此值）</summary>
    public required string Key { get; init; }

    /// <summary>角色属性（name, color, font, size, textcolor, textfont, typewriter）</summary>
    public required Dictionary<string, string> Properties { get; init; }
}

// ====== 样式定义 ======

/// <summary>
/// 样式定义语句——定义可复用的元素属性集合
/// <para>语法：style "style_name" color=#88CCFF size=18 fontFamily="Microsoft YaHei"</para>
/// </summary>
public sealed partial class StyleStmt : DslStatement
{
    /// <summary>样式名（元素通过 class= 引用）</summary>
    public required string Name { get; init; }

    /// <summary>样式属性字典</summary>
    public required Dictionary<string, string> Properties { get; init; }
}

// ====== 批量动画 ======

/// <summary>
/// 批量动画语句——同时对一个目标的多個属性执行动画
/// <para>语法：animate_block "target" x=100 y=200 opacity=0.5 duration=1.0 easing="EaseOutQuad"</para>
/// </summary>
public sealed partial class AnimateBlockStmt : DslStatement
{
    /// <summary>动画目标元素标识</summary>
    public required string Target { get; init; }

    /// <summary>动画属性列表（属性名, 目标值）</summary>
    public required List<(string Property, double Value)> Animations { get; init; }

    /// <summary>持续时间（秒），应用到所有属性</summary>
    public double? Duration { get; init; }

    /// <summary>缓动函数名，应用到所有属性</summary>
    public string? Easing { get; init; }
}

// ====== Call Screen ======

/// <summary>
/// 调用界面语句——对标 Ren'Py call screen
/// <para>语法：call_screen "scene_name" store="result_key"</para>
/// </summary>
public sealed partial class CallScreenStmt : DslStatement
{
    /// <summary>要调用的 UI 场景名</summary>
    public required string SceneName { get; init; }

    /// <summary>存储界面返回结果的变量键名</summary>
    public string? StoreKey { get; init; }

    /// <summary>传给 UI 场景的参数（key=value 对列表，Phase 24）</summary>
    public List<(string Key, string Value)>? Params { get; init; }
}

// ====== 场景元素（按序揭示）======

/// <summary>
/// 场景元素显示语句——scene 块内的 UI 元素行（image/text/button 等）
/// </summary>
public sealed partial class ShowElementStmt : DslStatement
{
    public required UIElementEntity Element { get; init; }
    //public required object Element { get; init; }
}

// ====== Phase 38: 时间事件与通知 ======

/// <summary>
/// 时间事件注册语句——注册游戏时间驱动的自动触发事件
/// <para>语法：</para>
/// <para>  指定天数：time_event day=5 hour=18 target="bounty_expired" desc="悬赏截止"</para>
/// <para>  每日重复：time_event hour=9 target="npc_daily" once=false desc="NPC每日事件"</para>
/// <para>  每周重复：time_event weekday="Mon,Thu" hour=6 target="shop_restock" once=false desc="商店补货"</para>
/// <para>  单次星期：time_event weekday="Mon" hour=12 target="special_event" once=true desc="特殊事件"</para>
/// <para>需要 EnableTimeSystem=true 才生效。</para>
/// </summary>
public sealed partial class TimeEventStmt : DslStatement
{
    /// <summary>触发的游戏天数（与 CurrentDay 同基准，默认 1-based）。设为 0 表示不按天数过滤。</summary>
    public int TriggerDay { get; init; }

    /// <summary>
    /// 触发的星期几（null 或空 = 不按星期过滤，优先于 TriggerDay）
    /// <para>支持缩写 Mon/Tue/Wed/Thu/Fri/Sat/Sun 或全称 Monday/Tuesday 等，逗号分隔多选。</para>
    /// </summary>
    public DayOfWeek[]? DaysOfWeek { get; init; }

    /// <summary>触发的小时（null=任意小时）</summary>
    public int? TriggerHour { get; init; }

    /// <summary>触发的分钟（null=任意分钟）</summary>
    public int? TriggerMinute { get; init; }

    /// <summary>触发时导航到的场景/label</summary>
    public required string Target { get; init; }

    /// <summary>是否只触发一次（默认 true）</summary>
    public bool IsOneShot { get; init; } = true;

    /// <summary>条件表达式（可选）</summary>
    public string? Condition { get; init; }

    /// <summary>事件描述（可选，用于日志）</summary>
    public string? Description { get; init; }
}

/// <summary>
/// 回调驱动时间事件注册语句——注册在指定时间执行的代码块
/// <para>语法：</para>
/// <para>  每日重复：set_time_event "noon_bell" 12 once=false</para>
/// <para>      say "钟声敲响了。"</para>
/// <para>  end</para>
/// <para>  周一单次：set_time_event "market" 8 once=true weekdays="Mon"</para>
/// <para>      say "集市开市了。"</para>
/// <para>  end</para>
/// <para>  第3天单次：set_time_event "intro" 12 minute=5 day=3 once=true</para>
/// <para>      say "第3天的事件触发了。"</para>
/// <para>  end</para>
/// <para>  每7天重复：set_time_event "rent" 9 day=7 once=false</para>
/// <para>      say "每7天的租金事件。"</para>
/// <para>  end</para>
/// <para>需要 EnableTimeSystem=true 才生效。</para>
/// </summary>
public sealed partial class SetTimeEventStmt : DslStatement
{
    /// <summary>事件 ID（强制手写，用于去重和存档）</summary>
    public required string Id { get; init; }

    /// <summary>触发小时（0-23）</summary>
    public int Hour { get; init; }

    /// <summary>触发分钟（null=整点触发，即 minute=0 时匹配）</summary>
    public int? Minute { get; init; }

    /// <summary>
    /// 天数约束（null=不限制天数）
    /// <para>当 IsOneShot=true 时：表示在第 Day 天触发（绝对天数，对应 CurrentDay）</para>
    /// <para>当 IsOneShot=false 时：表示每隔 Day 天触发一次（间隔天数，从游戏开始算起）</para>
    /// <para>与 DaysOfWeek 正交——同时指定时两者都需满足。</para>
    /// </summary>
    public int? Day { get; init; }

    /// <summary>星期几（null=每天，支持缩写 Mon/Tue/... 或全称，逗号分隔多选）</summary>
    public DayOfWeek[]? DaysOfWeek { get; init; }

    /// <summary>是否单次触发（默认 false=重复）</summary>
    public bool IsOneShot { get; init; } = false;

    /// <summary>条件表达式（可选）</summary>
    public string? Condition { get; init; }

    /// <summary>事件描述（可选）</summary>
    public string? Description { get; init; }
}

/// <summary>
/// 暂停游戏时间语句
/// <para>语法：time_pause</para>
/// </summary>
public sealed partial class TimePauseStmt : DslStatement { }

/// <summary>
/// 恢复游戏时间语句
/// <para>语法：time_resume</para>
/// </summary>
public sealed partial class TimeResumeStmt : DslStatement { }

/// <summary>
/// 批量跳过游戏时间语句
/// <para>语法：skip_time N</para>
/// <para>逐分钟 Tick，确保中间所有时间事件被检查。需要 EnableTimeSystem=true。</para>
/// </summary>
public sealed partial class SkipTimeStmt : DslStatement
{
    /// <summary>要跳过的分钟数</summary>
    public int Minutes { get; init; }
}

/// <summary>
/// 注销时间事件语句——手动删除已注册的时间事件
/// <para>语法：unregister_time_event "id" [permanent|temporary]</para>
/// <para>需要 EnableTimeSystem=true 才生效。</para>
/// <para>Phase 63 新增注销模式：permanent=永久销毁，temporary=暂时销毁（可恢复）。</para>
/// </summary>
public sealed partial class UnregisterTimeEventStmt : DslStatement
{
    /// <summary>要注销的事件 ID</summary>
    public required string Id { get; init; }

    /// <summary>
    /// 注销模式（Phase 63 新增）
    /// <para>Normal=正常注销（默认），Permanent=永久销毁，Temporary=暂时销毁</para>
    /// </summary>
    public UnregisterMode Mode { get; init; } = UnregisterMode.Normal;
}

/// <summary>
/// 恢复时间事件语句——恢复已注销的事件
/// <para>语法：restore_time_event "id"</para>
/// <para>Phase 63 新增——从全局注册表查回定义重新注册。</para>
/// <para>支持恢复 Temporary 模式注销的事件（清除标记后重新注册）</para>
/// <para>和 Normal 模式注销的 C# 声明式事件（直接重新注册）。</para>
/// <para>Permanent 模式注销的事件不可恢复。</para>
/// </summary>
public sealed partial class RestoreTimeEventStmt : DslStatement
{
    /// <summary>要恢复的事件 ID</summary>
    public required string Id { get; init; }
}

/// <summary>
/// 通知语句——显示 Toast 通知
/// <para>语法：notify "text" type=warning duration=5.0</para>
/// </summary>
public sealed partial class NotifyStmt : DslStatement
{
    /// <summary>通知文本</summary>
    public required string Text { get; init; }

    /// <summary>通知类型：info / warning / error（默认 info）</summary>
    public string? Type { get; init; }

    /// <summary>显示时长秒数（默认 3.0）</summary>
    public double? Duration { get; init; }
}

// ====== Phase 24: Ren'Py 功能对齐 ======

/// <summary>
/// window 窗口管理语句——对标 Ren'Py window auto/show/hide
/// </summary>
public sealed partial class WindowStmt : DslStatement
{
    /// <summary>窗口模式："auto" / "show" / "hide"</summary>
    public required string Mode { get; init; }
}

/// <summary>
/// 阻止回溯语句——对标 Ren'Py renpy.block_rollback()
/// </summary>
public sealed partial class BlockRollbackStmt : DslStatement { }

/// <summary>
/// 修复回溯语句——对标 Ren'Py renpy.fix_rollback()
/// </summary>
public sealed partial class FixRollbackStmt : DslStatement { }

/// <summary>
/// for 循环语句——对标 Ren'Py for x in [list]:
/// <para>语法：for "var" in {expr} { ... }</para>
/// </summary>
public sealed partial class ForStmt : DslStatement
{
    /// <summary>迭代变量名</summary>
    public required string VarName { get; init; }

    /// <summary>迭代源表达式（如 [1,2,3] 或 player.items）</summary>
    public required string SourceExpr { get; init; }
}

// ====== Phase 44: 叙事增强 ======

/// <summary>
/// switch 多分支语句——DSL 2.0
/// <para>语法：switch {expr}</para>
/// <para>编译为 if/else 链：将 switch 表达式存入临时变量，每个 case 编译为 BranchCommand 比较相等。</para>
/// </summary>
public sealed partial class SwitchStmt : DslStatement
{
    /// <summary>switch 表达式（不含花括号）</summary>
    public required string Expression { get; init; }
}

/// <summary>
/// switch case 分支——DSL 2.0
/// <para>语法：case N</para>
/// </summary>
public sealed partial class CaseStmt : DslStatement
{
    /// <summary>case 比较值（字符串形式，运行时与 switch 表达式求值结果比较）</summary>
    public required string Value { get; init; }
}

/// <summary>
/// switch default 分支——DSL 2.0
/// <para>语法：default</para>
/// </summary>
public sealed partial class DefaultStmt : DslStatement { }

/// <summary>
/// 函数定义语句——DSL 2.0
/// <para>语法：func name(param1, param2) { ... }</para>
/// <para>编译为 label + 参数列表存储。函数体作为 label 的内容执行。</para>
/// </summary>
public sealed partial class FuncStmt : DslStatement
{
    /// <summary>函数名（同时作为 label 名）</summary>
    public required string Name { get; init; }

    /// <summary>参数名列表</summary>
    public required List<string> Parameters { get; init; }
}

/// <summary>
/// 数组初始化语句——DSL 2.0
/// <para>语法：array "key" [item1, item2, ...] [once]</para>
/// </summary>
public sealed partial class ArrayStmt : DslStatement
{
    /// <summary>状态键名</summary>
    public required string Key { get; init; }

    /// <summary>初始元素列表（字符串形式，运行时解析类型）</summary>
    public required List<string> Items { get; init; }

    /// <summary>是否仅初始化一次（已存在则跳过）</summary>
    public bool IsDefine { get; init; }
}

/// <summary>
/// 数组追加语句——DSL 2.0
/// <para>语法：array_push "key" "value"</para>
/// </summary>
public sealed partial class ArrayPushStmt : DslStatement
{
    /// <summary>状态键名</summary>
    public required string Key { get; init; }

    /// <summary>追加的值（字符串形式，运行时解析）</summary>
    public required string ValuePart { get; init; }
}

/// <summary>
/// 数组弹出语句——DSL 2.0
/// <para>语法：array_pop "key"</para>
/// </summary>
public sealed partial class ArrayPopStmt : DslStatement
{
    /// <summary>状态键名</summary>
    public required string Key { get; init; }
}

/// <summary>
/// foreach 遍历语句——DSL 2.0
/// <para>语法：foreach "var" in "key" { ... }</para>
/// <para>编译为 for 循环，迭代源为数组状态键。</para>
/// </summary>
public sealed partial class ForeachStmt : DslStatement
{
    /// <summary>迭代变量名</summary>
    public required string VarName { get; init; }

    /// <summary>数组状态键名</summary>
    public required string SourceKey { get; init; }
}

/// <summary>
/// 字典初始化语句——DSL 2.0
/// <para>语法：dict "key" {"field":value,...} [once]</para>
/// </summary>
public sealed partial class DictStmt : DslStatement
{
    /// <summary>状态键名</summary>
    public required string Key { get; init; }

    /// <summary>字段列表（字段名, 值字符串）</summary>
    public required List<(string Field, string Value)> Fields { get; init; }

    /// <summary>是否仅初始化一次</summary>
    public bool IsDefine { get; init; }
}

/// <summary>
/// 字典设值语句——DSL 2.0
/// <para>语法：dict_set "key" "field" value</para>
/// </summary>
public sealed partial class DictSetStmt : DslStatement
{
    /// <summary>状态键名</summary>
    public required string Key { get; init; }

    /// <summary>字段名</summary>
    public required string Field { get; init; }

    /// <summary>值（字符串形式，运行时解析）</summary>
    public required string ValuePart { get; init; }
}

// ====== Phase 45: UI 增强 ======

/// <summary>
/// 弹窗语句——DSL 2.0
/// <para>语法：popup "name" width=N height=N mask=true { ... }</para>
/// </summary>
public sealed partial class PopupStmt : DslStatement
{
    /// <summary>弹窗名称</summary>
    public required string Name { get; init; }

    /// <summary>宽度（可选）</summary>
    public double? Width { get; init; }

    /// <summary>高度（可选）</summary>
    public double? Height { get; init; }

    /// <summary>是否显示遮罩（默认 true）</summary>
    public bool Mask { get; init; } = true;
}

/// <summary>
/// 层级设置语句——DSL 2.0
/// <para>语法：zindex N</para>
/// </summary>
public sealed partial class ZindexStmt : DslStatement
{
    /// <summary>Z-Index 值</summary>
    public int ZIndex { get; init; }
}

/// <summary>
/// 立绘显示语句——DSL 2.0
/// <para>语法：sprite "id" src="path" x=N y=N fade=N</para>
/// </summary>
public sealed partial class SpriteStmt : DslStatement
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Fade { get; init; }
}

/// <summary>
/// 立绘状态切换语句——DSL 2.0
/// <para>语法：sprite_state "id" emotion="smile"</para>
/// </summary>
public sealed partial class SpriteStateStmt : DslStatement
{
    public required string Id { get; init; }
    public required string Emotion { get; init; }
}

/// <summary>
/// 立绘移动语句——DSL 2.0
/// <para>语法：sprite_move "id" x=N y=N duration=N</para>
/// </summary>
public sealed partial class SpriteMoveStmt : DslStatement
{
    public required string Id { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Duration { get; init; }
}

/// <summary>
/// 立绘隐藏语句——DSL 2.0
/// <para>语法：sprite_hide "id" fade=N</para>
/// </summary>
public sealed partial class SpriteHideStmt : DslStatement
{
    public required string Id { get; init; }
    public double? Fade { get; init; }
}

/// <summary>
/// 背景切换语句——DSL 2.0
/// <para>语法：bg_switch "path" transition=fade duration=N</para>
/// </summary>
public sealed partial class BgSwitchStmt : DslStatement
{
    public required string Path { get; init; }
    public string? Transition { get; init; }
    public double? Duration { get; init; }
}

/// <summary>
/// 打字机速度设置语句——DSL 2.0
/// <para>语法：text_typewriter speed=N</para>
/// </summary>
public sealed partial class TextTypewriterStmt : DslStatement
{
    /// <summary>打字机速度（字符/秒），0=关闭打字机</summary>
    public double Speed { get; init; }
}

// ====== Phase 46: Live2D ======

/// <summary>Live2D 模型注册语句</summary>
public sealed partial class Live2DCharStmt : DslStatement
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public double? Height { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Fade { get; init; }
    public bool Loop { get; init; } = true;
    public bool Seamless { get; init; }
    public double BlinkRate { get; init; } = 3.0;
    public bool MouseTrackHead { get; init; } = true;
    public bool VoiceSyncMouth { get; init; } = true;
}

public sealed partial class Live2DShowStmt : DslStatement
{
    public required string Id { get; init; }
}

public sealed partial class Live2DMotionStmt : DslStatement
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public double? Fade { get; init; }
    public bool Loop { get; init; } = true;
}

public sealed partial class Live2DExprStmt : DslStatement
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public double? Fade { get; init; }
}

public sealed partial class Live2DParamStmt : DslStatement
{
    public required string Id { get; init; }
    public required string ParamName { get; init; }
    public double Value { get; init; }
    public double Weight { get; init; } = 1.0;
}

public sealed partial class Live2DHideStmt : DslStatement
{
    public required string Id { get; init; }
    public double? Fade { get; init; }
}

public sealed partial class Live2DPauseStmt : DslStatement
{
    public required string Id { get; init; }
}

public sealed partial class Live2DResumeStmt : DslStatement
{
    public required string Id { get; init; }
}

// ====== Phase 47: 存档/成就/章节 ======

/// <summary>
/// 自动存档语句——DSL 2.0
/// <para>语法：auto_save true / auto_save false</para>
/// </summary>
public sealed partial class AutoSaveStmt : DslStatement
{
    public bool Enabled { get; init; }
}

/// <summary>
/// 删除存档语句——DSL 2.0
/// <para>语法：save_delete "slot"</para>
/// </summary>
public sealed partial class SaveDeleteStmt : DslStatement
{
    public required string SlotId { get; init; }
}

/// <summary>
/// 章节解锁语句——DSL 2.0
/// <para>语法：chapter "id" name "章节名" unlock=true</para>
/// </summary>
public sealed partial class ChapterStmt : DslStatement
{
    public required string Id { get; init; }
    public string? ChapterName { get; init; }
    public bool Unlock { get; init; } = true;
}

/// <summary>
/// 成就解锁语句——DSL 2.0
/// <para>语法：achievement "id" name "成就名"</para>
/// </summary>
public sealed partial class AchievementStmt : DslStatement
{
    public required string Id { get; init; }
    public string? AchievementName { get; init; }
}

/// <summary>
/// 自动模式速度设置语句——DSL 2.0
/// <para>语法：auto_speed N</para>
/// </summary>
public sealed partial class AutoSpeedStmt : DslStatement
{
    public double Speed { get; init; }
}

/// <summary>
/// 禁止跳过语句——DSL 2.0
/// <para>语法：no_skip</para>
/// </summary>
public sealed partial class NoSkipStmt : DslStatement { }

/// <summary>
/// 强制跳过语句——DSL 2.0
/// <para>语法：force_skip</para>
/// </summary>
public sealed partial class ForceSkipStmt : DslStatement { }

// ====== Phase 48: 视频增强 ======

/// <summary>
/// 视频跳过设置语句——DSL 2.0
/// <para>语法：video_skipable true / video_skipable false</para>
/// </summary>
public sealed partial class VideoSkipableStmt : DslStatement
{
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// 视频结束后自动导航语句——DSL 2.0
/// <para>语法：video_auto_nav "scene_name"</para>
/// </summary>
public sealed partial class VideoAutoNavStmt : DslStatement
{
    public required string SceneName { get; init; }
}
