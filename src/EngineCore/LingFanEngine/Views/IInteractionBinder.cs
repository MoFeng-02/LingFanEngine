using Avalonia.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// 交互绑定接口——为控件挂载 nav/cmd/hover/selected/disabled 交互。
/// </summary>
public interface IInteractionBinder
{
    /// <summary>
    /// 为控件绑定交互行为（点击导航、命令执行、悬停效果、选中切换、禁用状态）
    /// </summary>
    void ApplyInteraction(Control control, Dictionary<string, object> props);
}
