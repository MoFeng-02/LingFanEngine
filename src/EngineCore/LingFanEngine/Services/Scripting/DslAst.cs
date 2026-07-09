using LingFanEngine.Abstractions.Entities.UIs;

namespace LingFanEngine.Services.Scripting;

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

public sealed partial class ReturnStmt : DslStatement { }

// ====== 对话与导航 ======

public sealed partial class SayStmt : DslStatement
{
    public required string Text { get; init; }
    public string? Speaker { get; init; }
    /// <summary>对话期间场景按钮是否可点击（say clickable=true / say okey）</summary>
    public bool Clickable { get; init; }
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

// ====== 媒体 ======

public sealed partial class BgmStmt : DslStatement
{
    public required string Path { get; init; }
    public float? Volume { get; init; }
}

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
/// </para>对标 Python break。
/// </summary>
public sealed partial class BreakStmt : DslStatement { }

/// <summary>
/// continue 语句——跳过当前迭代，进入下一次循环
/// </para>对标 Python continue。
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
    public required string ImagePath { get; init; }
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
    public bool IsClear { get; init; }
}

/// <summary>
/// 角色定义语句——定义角色对话样式（颜色/字体/名字等）
/// <para>语法：character "speaker_key" name="显示名" color="#FF4444" font="Microsoft YaHei" ...</para>
/// <para>编译为 SetVariableCommand，存储到 __characters[speaker_key] 字典。</para>
/// <para>say 语句通过 speaker 匹配 character 定义，自动应用样式。</para>
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
/// <para>编译为 SetVariableCommand，存储到 __style_{name} 字典。</para>
/// <para>元素通过 class="style_name" 引用样式，样式属性作为默认值，元素自身属性覆盖。</para>
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
/// <para>编译为多个 AnimateCommand（每属性一个），并行执行。</para>
/// <para>x/y/opacity/rotate/scale 为动画属性，duration/easing 为全局参数。</para>
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
/// <para>导航到 UI 场景，等待用户交互完成，结果存储到指定变量。</para>
/// <para>与 navigate 的区别：call_screen 阻塞等待界面返回，不进入场景堆栈。</para>
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
/// <para>编译为 ShowElementCommand，由 DslExecutor 按序追加到 Scene.Elements。</para>
/// <para>阻塞命令（say/transition）之前的元素立即显示，之后的元素等阻塞完成才显示。</para>
/// </summary>
public sealed partial class ShowElementStmt : DslStatement
{
    public required UIElementEntity Element { get; init; }
}

// ====== Phase 24: Ren'Py 功能对齐 ======

/// <summary>
/// window 窗口管理语句——对标 Ren'Py window auto/show/hide
/// <para>语法：window auto | window show | window hide</para>
/// <para>auto: 对话时自动显示，无对话时自动隐藏（默认）</para>
/// <para>show: 强制显示对话框</para>
/// <para>hide: 强制隐藏对话框</para>
/// </summary>
public sealed partial class WindowStmt : DslStatement
{
    /// <summary>窗口模式："auto" / "show" / "hide"</summary>
    public required string Mode { get; init; }
}

/// <summary>
/// 阻止回溯语句——对标 Ren'Py renpy.block_rollback()
/// <para>设置回溯阻止标记，后续检查点不再创建，防止用户回退查看不同选择。</para>
/// </summary>
public sealed partial class BlockRollbackStmt : DslStatement { }

/// <summary>
/// 修复回溯语句——对标 Ren'Py renpy.fix_rollback()
/// <para>清除回溯阻止标记，恢复正常检查点创建。</para>
/// </summary>
public sealed partial class FixRollbackStmt : DslStatement { }

/// <summary>
/// for 循环语句——对标 Ren'Py for x in [list]:
/// <para>语法：for "var" in {expr} { ... }</para>
/// <para>迭代源可以是列表表达式或范围表达式。编译为 while + 索引变量。</para>
/// </summary>
public sealed partial class ForStmt : DslStatement
{
    /// <summary>迭代变量名</summary>
    public required string VarName { get; init; }

    /// <summary>迭代源表达式（如 [1,2,3] 或 player.items）</summary>
    public required string SourceExpr { get; init; }
}
