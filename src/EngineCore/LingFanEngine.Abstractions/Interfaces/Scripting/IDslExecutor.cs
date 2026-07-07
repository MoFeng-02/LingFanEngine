using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.Interfaces.Scripting;

/// <summary>
/// DSL 执行器接口
/// <para>异步优先：RunAsync 为主执行循环，遇到交互命令（say/menu/input/wait）时
/// 使用 async/await 天然等待，无需帧轮询。</para>
/// <para>所有状态保存在状态容器中，支持 Say 级回溯。</para>
/// </summary>
public interface IDslExecutor
{
    /// <summary>注册 StoryRegistry（用于自动解析 label 所在文件）</summary>
    void SetStoryRegistry(IStoryRegistry registry);

    /// <summary>加载命令列表和标签索引</summary>
    /// <param name="preserveCheckpoints">true=保留现有检查点（跨场景导航时用），false=清除（新故事/读档）</param>
    void LoadCommands(IReadOnlyList<ICommand> commands, IReadOnlyDictionary<string, int>? labels = null, bool preserveCheckpoints = false);

    /// <summary>从头开始执行（启动 async task）</summary>
    void Start();

    /// <summary>从指定 label 开始执行（启动 async task）</summary>
    void StartFromLabel(string label);

    /// <summary>停止当前执行（取消 async task）</summary>
    void Stop();

    /// <summary>是否正在执行</summary>
    bool IsRunning { get; }

    // ========== Say 级回溯 ==========

    /// <summary>是否可以回溯</summary>
    bool CanRollback();

    /// <summary>是否可以前进</summary>
    bool CanRollforward();

    /// <summary>回溯到指定检查点位置</summary>
    bool RollbackTo(int targetPos);

    /// <summary>后退到上一个检查点</summary>
    bool Rollback();

    /// <summary>前进到下一个检查点</summary>
    bool Rollforward();

    /// <summary>清除所有回溯检查点</summary>
    void ClearCheckpoints();
}
