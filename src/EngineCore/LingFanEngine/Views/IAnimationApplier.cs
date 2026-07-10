using Avalonia.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// 动画应用接口——每帧读取 __anim_*_current 状态，更新运行时控件的 Transform/Opacity。
/// </summary>
public interface IAnimationApplier
{
    /// <summary>
    /// 扫描所有活动动画状态，更新对应控件的变换属性
    /// </summary>
    /// <param name="sceneRoot">场景根容器（从中遍历子控件按 Tag 匹配动画目标）</param>
    void Apply(Panel? sceneRoot);

    /// <summary>
    /// 场景重建后调用，重建 Tag→Control 查找表并清理缓存
    /// </summary>
    void RebuildControlMap(Panel? sceneRoot);
}
