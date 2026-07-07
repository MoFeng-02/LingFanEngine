using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Services.Core;

/// <summary>
/// 屏幕震动服务实现
/// <para>每帧推进震动状态，计算随机偏移量，到期后归零。</para>
/// </summary>
public class ShakeService : IShakeService
{
    /// <inheritdoc/>
    public void Update(double frameDelta, IStateContainer state)
    {
        if (!state.Get<bool>(StateKeys.Shake.Active)) return;

        var elapsed = state.Get<double>(StateKeys.Shake.Elapsed) + frameDelta;
        var duration = state.Get<double>(StateKeys.Shake.Duration);
        var intensity = state.Get<double>(StateKeys.Shake.Intensity);

        if (elapsed >= duration)
        {
            // 震动结束
            state.Set(StateKeys.Shake.Active, false);
            state.Set(StateKeys.Shake.OffsetX, 0.0);
            state.Set(StateKeys.Shake.OffsetY, 0.0);
            state.Set(StateKeys.Shake.Elapsed, 0.0);
        }
        else
        {
            // 衰减系数：随时间推移震动幅度递减
            var decay = 1.0 - (elapsed / duration);
            var currentIntensity = intensity * decay;
            // 随机偏移（使用简单的伪随机）
            var rng = Random.Shared;
            state.Set(StateKeys.Shake.OffsetX, (rng.NextDouble() * 2 - 1) * currentIntensity);
            state.Set(StateKeys.Shake.OffsetY, (rng.NextDouble() * 2 - 1) * currentIntensity);
            state.Set(StateKeys.Shake.Elapsed, elapsed);
        }
    }
}
