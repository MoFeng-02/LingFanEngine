namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 屏幕震动服务接口
/// <para>每帧推进震动状态，计算随机偏移量，到期后归零。</para>
/// </summary>
public interface IShakeService
{
    /// <summary>
    /// 每帧更新屏幕震动
    /// </summary>
    /// <param name="frameDelta">帧间隔（秒）</param>
    /// <param name="state">状态容器</param>
    void Update(double frameDelta, IStateContainer state);
}
