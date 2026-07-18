using LingFanEngine.Abstractions.Entities.Events;
using LingFanEngine.Abstractions.Interfaces.Scripting;

namespace LingFanEngine.Abstractions.Interfaces.Events;

/// <summary>
/// DSL 全局时间事件注册表接口（Phase 63 新增）
/// <para>启动时预过滤 + 并行编译含 set_time_event 的 .story 文件，提取事件注册信息。</para>
/// <para>读档重注册时按 ID 查表，解决跨场景事件丢失问题。</para>
/// <para>restore_time_event 时按 ID 查回定义重新注册。</para>
/// <para>设计理念：时间事件生命周期——事件一旦注册即独立，场景只是挂载器（出生地）。</para>
/// </summary>
public interface ITimeEventRegistry
{
    /// <summary>按 ID 查找 DSL 编译的时间事件注册信息</summary>
    /// <param name="id">事件 ID</param>
    /// <param name="registration">查找到的注册信息（含子块命令）</param>
    /// <returns>true=找到，false=未找到</returns>
    bool TryGetRegistration(string id, out TimeEventRegistration registration);

    /// <summary>获取所有已注册的事件 ID</summary>
    IReadOnlyCollection<string> GetAllIds();

    /// <summary>注册表是否已完成初始化加载</summary>
    bool IsLoaded { get; }

    /// <summary>异步初始化——扫描并编译所有含 set_time_event 的 .story 文件</summary>
    /// <param name="storyRegistry">故事注册表（提供 .story 文件访问）</param>
    /// <param name="scriptEngine">脚本引擎（编译 .story 文件）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task InitializeAsync(IStoryRegistry storyRegistry, IScriptEngine scriptEngine, CancellationToken cancellationToken = default);

    /// <summary>
    /// 注册 C# 声明式时间事件（来自 SceneScriptEntry.TimeEvents / InTimeEvents()）
    /// <para>Phase 63 修复——C# 声明式事件也纳入全局注册表，使 restore_time_event 和读档重注册
    /// 能跨场景查找事件定义，符合"时间事件生命周期"理念。</para>
    /// <para>同 ID 重复注册 → 覆盖（创作者责任）。</para>
    /// </summary>
    /// <param name="registration">事件注册信息</param>
    void RegisterDeclaration(TimeEventRegistration registration);

    /// <summary>
    /// 获取所有 C# 声明式事件（来自 InTimeEvents()）
    /// <para>Phase 63 修复——用于 ResetGameState 后恢复 C# 声明式事件。</para>
    /// <para>不含 DSL 事件（DSL 事件由场景执行 set_time_event 自然恢复）。</para>
    /// </summary>
    /// <returns>C# 声明式事件注册信息集合</returns>
    IReadOnlyCollection<TimeEventRegistration> GetAllDeclarations();
}
