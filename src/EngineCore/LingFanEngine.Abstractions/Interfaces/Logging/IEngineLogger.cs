using System.Runtime.CompilerServices;

namespace LingFanEngine.Abstractions.Interfaces.Logging;

/// <summary>
/// 引擎诊断日志接口——面向引擎开发者排查问题。
/// <para>默认实现通过 DI 注册，游戏层可替换为自定义实现。</para>
/// <para>与 <see cref="Core.IDebugConsoleService"/>（游戏内调试面板）正交，职责不同：</para>
/// <para>- IEngineLogger：引擎自身诊断日志，面向开发者排查问题</para>
/// <para>- IDebugConsoleService：游戏内调试面板，面向玩家/开发者查看运行时状态</para>
/// <para>AOT 友好：无反射、无动态代码生成，Caller 信息由编译期填充。</para>
/// </summary>
public interface IEngineLogger
{
    /// <summary>日志分类名（如 "StoryLoader"、"DslExecutor"）</summary>
    string Category { get; }

    /// <summary>当前最低输出级别（低于此级别的日志被丢弃，零开销）</summary>
    EngineLogLevel MinimumLevel { get; set; }

    /// <summary>是否启用指定级别</summary>
    bool IsEnabled(EngineLogLevel level);

    /// <summary>
    /// 记录日志（异常可选）。
    /// <para>Caller 信息由编译器自动填充，AOT 安全。</para>
    /// </summary>
    /// <param name="level">日志级别</param>
    /// <param name="message">日志消息</param>
    /// <param name="exception">关联异常（可选）</param>
    /// <param name="member">调用方成员名（编译器填充）</param>
    /// <param name="file">调用方文件路径（编译器填充）</param>
    /// <param name="line">调用方行号（编译器填充）</param>
    void Log(
        EngineLogLevel level,
        string message,
        Exception? exception = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0);
}
