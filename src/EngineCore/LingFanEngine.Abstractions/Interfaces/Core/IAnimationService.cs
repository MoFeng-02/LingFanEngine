namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 控件级动画服务接口
/// <para>每帧推进所有 active 的 animate 动画，应用缓动函数。</para>
/// </summary>
public interface IAnimationService
{
    /// <summary>
    /// 每帧推进所有活跃的控件动画
    /// </summary>
    /// <param name="frameDelta">帧间隔（秒）</param>
    /// <param name="state">状态容器</param>
    void Update(double frameDelta, IStateContainer state);
}
