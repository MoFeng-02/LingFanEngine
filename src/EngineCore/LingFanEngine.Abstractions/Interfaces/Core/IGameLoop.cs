namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 游戏主循环接口
/// <para>负责帧循环调度：命令消费 → 状态更新 → 补间插值 → 渲染。</para>
/// </summary>
public interface IGameLoop : IDisposable
{
    /// <summary>
    /// 启动主循环
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 停止主循环
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 当前是否运行中
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 目标帧率（移动端 60，桌面端 120）
    /// </summary>
    int TargetFps { get; set; }

    /// <summary>
    /// 帧回调：每帧渲染前调用
    /// </summary>
    event Action<double>? OnFrame;

    /// <summary>
    /// 错误回调：内部错误时调用
    /// method
    /// </summary>
    event Action<Exception, string>? OnException;
}
