using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 调试控制台服务实现
/// <para>所有调试日志存储在状态容器中（__debug_logs），</para>
/// <para>通过 SaveSystemState 持久化。UI 层通过 StateKeys.Debug 读取渲染调试面板。</para>
/// </summary>
public class DebugConsoleService : IDebugConsoleService
{
    private readonly IStateContainer _state;

    public DebugConsoleService(IStateContainer state)
    {
        _state = state;
        EnsureDefaults();
    }

    /// <summary>确保默认状态</summary>
    private void EnsureDefaults()
    {
        if (!_state.ContainsKey(StateKeys.Debug.Logs))
            _state.Set(StateKeys.Debug.Logs, new List<DebugLogEntry>());
        if (!_state.ContainsKey(StateKeys.Debug.Visible))
            _state.Set(StateKeys.Debug.Visible, false);
        if (!_state.ContainsKey(StateKeys.Debug.Enabled))
            _state.Set(StateKeys.Debug.Enabled, false);
        if (!_state.ContainsKey(StateKeys.Debug.MaxLogs))
            _state.Set(StateKeys.Debug.MaxLogs, 500);
    }

    /// <inheritdoc/>
    public void Log(string level, string message, string? source = null)
    {
        // 未启用调试模式时不记录（减少运行时开销）
        if (!_state.Get<bool>(StateKeys.Debug.Enabled)) return;

        var logs = _state.Get<List<DebugLogEntry>>(StateKeys.Debug.Logs) ?? [];
        var maxLogs = _state.Get<int>(StateKeys.Debug.MaxLogs);
        if (maxLogs <= 0) maxLogs = 500;

        logs.Add(new DebugLogEntry
        {
            Level = level,
            Message = message,
            Source = source
        });

        // 超出上限时移除最旧条目
        while (logs.Count > maxLogs)
            logs.RemoveAt(0);

        _state.Set(StateKeys.Debug.Logs, logs);
    }

    /// <inheritdoc/>
    public List<DebugLogEntry> GetLogs() =>
        _state.Get<List<DebugLogEntry>>(StateKeys.Debug.Logs) ?? [];

    /// <inheritdoc/>
    public void ClearLogs()
    {
        _state.Set(StateKeys.Debug.Logs, new List<DebugLogEntry>());
    }

    /// <inheritdoc/>
    public bool IsEnabled
    {
        get => _state.Get<bool>(StateKeys.Debug.Enabled);
        set => _state.Set(StateKeys.Debug.Enabled, value);
    }

    /// <inheritdoc/>
    public bool IsVisible
    {
        get => _state.Get<bool>(StateKeys.Debug.Visible);
        set => _state.Set(StateKeys.Debug.Visible, value);
    }
}
