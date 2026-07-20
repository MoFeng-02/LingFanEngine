using LingFanEngine.Abstractions.EngineOptions;
using LingFanEngine.Abstractions.Interfaces.Entry;
using LingFanEngine.Abstractions.Interfaces.Events;
using LingFanEngine.Abstractions.Interfaces.Media;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Interfaces.Scripting;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Scripting;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 命令处理上下文 — 提供处理器所需的所有依赖和共享操作
/// <para>由 GameLoop 构造并传入，确保处理器无状态、可复用。</para>
/// <para>所有属性均为接口类型，不暴露具体实现类。</para>
/// </summary>
public interface ICommandContext
{
    // ========== 依赖 ==========

    /// <summary>状态容器（SSOT）</summary>
    IStateContainer State { get; }

    /// <summary>命令管道（可投递新命令）</summary>
    ICommandPipeline Pipeline { get; }

    /// <summary>场景注册表</summary>
    ISceneRegistry? SceneRegistry { get; }

    /// <summary>场景堆栈</summary>
    ISceneStack? SceneStack { get; }

    /// <summary>故事注册表</summary>
    IStoryRegistry? StoryRegistry { get; }

    /// <summary>DSL 执行器</summary>
    IDslExecutor? DslExecutor { get; }

    /// <summary>过渡动画引擎</summary>
    ITransitionEngine? TransitionEngine { get; }

    /// <summary>音频管理器</summary>
    IAudioManager? AudioManager { get; }

    /// <summary>视频管理器</summary>
    IVideoManager? VideoManager { get; }

    /// <summary>事件调度器（时间事件）</summary>
    IEventScheduler? EventScheduler { get; }

    /// <summary>DSL 全局时间事件注册表（Phase 63，用于 restore_time_event 查回定义）</summary>
    ITimeEventRegistry? TimeEventRegistry { get; }

    /// <summary>游戏时间服务</summary>
    IGameTimeService? TimeService { get; }

    /// <summary>国际化翻译服务（原文→译文，null 时不翻译）</summary>
    II18nService? I18n { get; }

    /// <summary>存档服务</summary>
    ISaveService? SaveService { get; }

    /// <summary>引擎选项</summary>
    LingFanEngineOptions Options { get; }

    /// <summary>截图委托（用于存档缩略图）</summary>
    Func<byte[]?>? CaptureThumbnail { get; }

    // ========== 查询 ==========

    /// <summary>查找已注册的 C# StoryScript 场景入口</summary>
    bool TryGetScriptEntry(string sceneName, out SceneScriptEntry? entry);

    // ========== 共享操作 ==========

    /// <summary>场景切换时重置交互状态（对话/菜单/输入/过渡/动画）</summary>
    void ResetInteractionState();

    /// <summary>清空所有 _local_ 前缀的局部变量</summary>
    void ClearLocalVariables();

    /// <summary>从当前状态构建存档数据（返回 null 表示当前场景不允许存档）</summary>
    SaveData? BuildSaveData();

    /// <summary>应用存档数据到状态容器</summary>
    void ApplySaveData(SaveData data);

    /// <summary>
    /// 应用存档数据到状态容器
    /// </summary>
    /// <param name="continueGame">true=继续游戏（精确恢复），false=锚点读取（从头执行）</param>
    void ApplySaveData(SaveData data, bool continueGame);

    /// <summary>报告异常（触发 OnException 事件）</summary>
    void ReportException(Exception ex, string source);
}
