using LingFanEngine.Abstractions.Entities.Transitions;

namespace LingFanEngine.Abstractions.Interfaces.Core;

/// <summary>
/// 过渡动画引擎接口
/// <para>所有状态在状态容器中（__transition_*），不维护私有字段。</para>
/// </summary>
public interface ITransitionEngine
{
    /// <summary>是否为活跃状态（从状态容器读取）</summary>
    bool IsActive { get; }

    /// <summary>开始一个过渡动画</summary>
    void StartTransition(TransitionEntity? transition);

    /// <summary>逐帧更新，由 GameLoop 每帧调用</summary>
    void Update(double deltaTime);

    /// <summary>立即结束过渡</summary>
    void CompleteTransition();
}
