using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Abstractions.Models;
using LingFanEngine.Abstractions.Scripting;
using LingFanEngine.Extensions;
using LingFanEngine.Services.Media;
using LingFanEngine.Services.Scripting;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 命令处理器接口 — 每种 ICommand 类型对应一个处理器
/// <para>遵循开闭原则：新增命令类型只需新增处理器并注册，无需修改 GameLoop。</para>
/// </summary>
/// <typeparam name="TCommand">处理的命令类型</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>
    /// 处理命令
    /// </summary>
    /// <param name="command">命令实例</param>
    /// <param name="ctx">处理上下文（提供所有引擎依赖）</param>
    void Handle(TCommand command, ICommandContext ctx);
}

/// <summary>
/// 命令处理上下文 — 提供处理器所需的所有依赖和共享操作
/// <para>由 GameLoop 构造并传入，确保处理器无状态、可复用。</para>
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
    SceneStack? SceneStack { get; }

    /// <summary>故事注册表</summary>
    StoryRegistry? StoryRegistry { get; }

    /// <summary>DSL 执行器</summary>
    DslExecutor? DslExecutor { get; }

    /// <summary>过渡动画引擎</summary>
    TransitionEngine? TransitionEngine { get; }

    /// <summary>音频管理器</summary>
    AudioManager? AudioManager { get; }

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

    /// <summary>从当前状态构建存档数据</summary>
    SaveData BuildSaveData();

    /// <summary>应用存档数据到状态容器</summary>
    void ApplySaveData(SaveData data);

    /// <summary>报告异常（触发 OnException 事件）</summary>
    void ReportException(Exception ex, string source);
}
