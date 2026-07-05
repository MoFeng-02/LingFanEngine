using LingFanEngine.Abstractions.Models;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 调试控制台服务接口
/// <para>负责调试日志记录、查询和调试面板状态管理。</para>
/// <para>所有数据存储在状态容器中，UI 层通过 StateKeys.Debug 读取。</para>
/// </summary>
public interface IDebugConsoleService
{
    /// <summary>记录调试日志</summary>
    /// <param name="level">日志级别（Info/Warning/Error/Debug）</param>
    /// <param name="message">日志消息</param>
    /// <param name="source">来源（可选）</param>
    void Log(string level, string message, string? source = null);

    /// <summary>获取所有调试日志</summary>
    List<DebugLogEntry> GetLogs();

    /// <summary>清空所有调试日志</summary>
    void ClearLogs();

    /// <summary>调试模式是否开启</summary>
    bool IsEnabled { get; set; }

    /// <summary>调试面板是否可见</summary>
    bool IsVisible { get; set; }
}
