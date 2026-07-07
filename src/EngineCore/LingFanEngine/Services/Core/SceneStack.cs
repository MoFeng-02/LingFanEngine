using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models.Saves;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 场景堆栈——管理历史导航记录，支持前后跳转和完整状态回退
/// <para>每次导航时保存当前场景名 + 世界状态快照。</para>
/// <para>back = 弹出栈顶恢复场景 + 状态，forward = 重新压入。</para>
/// <para>最大深度由 MaxDepth 控制（默认 50），超出时删除最旧记录。</para>
/// </summary>
public class SceneStack : ISceneStack
{
    private readonly IStateContainer _state;
    private const string KeyStack = StateKeys.Scene.Stack;
    private const string KeyForward = StateKeys.Scene.ForwardStack;
    private const int DefaultMaxDepth = 50;

    /// <summary>最大堆栈深度（超出时丢弃最旧记录）</summary>
    public int MaxDepth { get; set; } = DefaultMaxDepth;

    public SceneStack(IStateContainer state)
    {
        _state = state;
    }

    private List<SceneSnapshot> GetStack()
    {
        var stack = _state.Get<List<SceneSnapshot>>(KeyStack);
        if (stack == null)
        {
            stack = new List<SceneSnapshot>();
            _state.Set(KeyStack, stack);
        }
        return stack;
    }

    private List<SceneSnapshot> GetForwardStack()
    {
        var stack = _state.Get<List<SceneSnapshot>>(KeyForward);
        if (stack == null)
        {
            stack = new List<SceneSnapshot>();
            _state.Set(KeyForward, stack);
        }
        return stack;
    }

    /// <summary>
    /// 保存当前世界状态快照（全量，仅排除 __ 系统变量）
    /// </summary>
    private Dictionary<string, object?> CaptureState()
    {
        var snapshot = new Dictionary<string, object?>();
        // 从状态容器获取所有 key（通过 IStateContainer.GetSnapshot）
        var allState = _state.GetSnapshot();
        foreach (var (k, v) in allState)
        {
            // 跳过系统变量（__ 前缀）
            if (!string.IsNullOrEmpty(k) && k.StartsWith(StateKeys.SystemPrefix))
                continue;
            snapshot[k] = v;
        }
        return snapshot;
    }

    /// <summary>
    /// 恢复世界状态快照（先清除非系统变量，再写入快照内容）
    /// </summary>
    private void RestoreState(Dictionary<string, object?> snapshot)
    {
        // 1. 清除当前所有非系统、非局部变量（防止残留脏数据）
        var allState = _state.GetSnapshot();
        foreach (var (k, _) in allState)
        {
            if (!string.IsNullOrEmpty(k)
                && !k.StartsWith(StateKeys.SystemPrefix)
                && !k.StartsWith("_local_"))
                _state.Remove(k);
        }

        // 2. 写入快照内容
        foreach (var (k, v) in snapshot)
            _state.Set(k, v);
    }

    /// <summary>
    /// 推入当前场景快照（导航前调）
    /// </summary>
    public void Push(string sceneName)
    {
        var stack = GetStack();
        // 不重复推入相同场景
        if (stack.Count > 0 && stack[^1].SceneName == sceneName)
            return;

        var snapshot = new SceneSnapshot
        {
            SceneName = sceneName,
            State = CaptureState()
        };
        stack.Add(snapshot);
        Trim();

        // 清空前进栈（新导航产生后，前进历史无效）
        _state.Set(KeyForward, new List<SceneSnapshot>());
    }

    /// <summary>
    /// 后退：弹出栈顶，恢复其场景和状态
    /// </summary>
    public SceneSnapshot? Back()
    {
        var stack = GetStack();
        if (stack.Count == 0) return null;

        // 当前场景状态压入前进栈
        var currentSc = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";
        if (!string.IsNullOrEmpty(currentSc))
        {
            var forwardStack = GetForwardStack();
            forwardStack.Add(new SceneSnapshot
            {
                SceneName = currentSc,
                State = CaptureState()
            });
        }

        var last = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        RestoreState(last.State);
        return last;
    }

    /// <summary>
    /// 前进：恢复之前后退时弹出的状态
    /// </summary>
    public SceneSnapshot? Forward()
    {
        var forwardStack = GetForwardStack();
        if (forwardStack.Count == 0) return null;

        // 当前场景状态压入后退栈
        var currentSc = _state.Get<string>(StateKeys.Scene.CurrentName) ?? "";
        if (!string.IsNullOrEmpty(currentSc))
        {
            var stack = GetStack();
            stack.Add(new SceneSnapshot
            {
                SceneName = currentSc,
                State = CaptureState()
            });
        }

        var next = forwardStack[^1];
        forwardStack.RemoveAt(forwardStack.Count - 1);
        RestoreState(next.State);
        return next;
    }

    /// <summary>
    /// 查看上一个场景（不弹出）
    /// </summary>
    public SceneSnapshot? Peek()
    {
        var stack = GetStack();
        return stack.Count > 0 ? stack[^1] : null;
    }

    /// <summary>
    /// 清空堆栈（scene 命令时调）
    /// </summary>
    public void Clear()
    {
        _state.Set(KeyStack, new List<SceneSnapshot>());
        _state.Set(KeyForward, new List<SceneSnapshot>());
    }

    /// <summary>
    /// 当前堆栈深度
    /// </summary>
    public int Count => _state.Get<List<SceneSnapshot>>(KeyStack)?.Count ?? 0;

    /// <summary>
    /// 获取完整堆栈快照（用于存档）
    /// </summary>
    public IReadOnlyList<SceneSnapshot> Snapshot => GetStack().AsReadOnly();

    /// <summary>
    /// 用快照恢复堆栈（读档时调）
    /// </summary>
    public void Restore(IReadOnlyList<SceneSnapshot> snapshot)
    {
        _state.Set(KeyStack, new List<SceneSnapshot>(snapshot));
        _state.Set(KeyForward, new List<SceneSnapshot>());
    }

    private void Trim()
    {
        var stack = GetStack();
        while (stack.Count > MaxDepth)
            stack.RemoveAt(0);
    }
}
