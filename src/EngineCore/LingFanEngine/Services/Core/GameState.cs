using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 只读游戏状态实现
/// <para>包装 IStateContainer 的快照，提供给 DLC/Mod 的安全只读视图。</para>
/// </summary>
public class GameState : IReadOnlyGameState
{
    private readonly IStateContainer _state;

    /// <summary>
    /// 构造函数
    /// </summary>
    public GameState(IStateContainer state)
    {
        _state = state;
    }

    /// <inheritdoc/>
    public T? Get<T>(string key) => _state.Get<T>(key);

    /// <inheritdoc/>
    public bool TryGet<T>(string key, out T? value) => _state.TryGet(key, out value);

    /// <inheritdoc/>
    public bool ContainsKey(string key) => _state.ContainsKey(key);

    /// <inheritdoc/>
    public IEnumerable<string> Keys => _state.Keys;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object?> GetSnapshot() => _state.GetSnapshot();

    /// <inheritdoc/>
    public long GameTotalMinutes => _state.Get<long>(StateKeys.GameTime.TotalMinutes);

    /// <inheritdoc/>
    public string? CurrentPath => _state.Get<string>(StateKeys.Story.CurrentPath);

    /// <inheritdoc/>
    public string? CurrentSceneName => _state.Get<string>(StateKeys.Scene.CurrentName);
}
