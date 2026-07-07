namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 播放控制服务接口
/// <para>处理 Skip/Auto 模式的每帧逻辑——DSL 执行器异步运行时调用。</para>
/// </summary>
public interface IPlaybackService
{
    /// <summary>
    /// 处理 Skip/Auto 播放模式
    /// </summary>
    /// <param name="frameDelta">帧间隔（秒）</param>
    /// <param name="state">状态容器</param>
    void Process(double frameDelta, IStateContainer state);
}
