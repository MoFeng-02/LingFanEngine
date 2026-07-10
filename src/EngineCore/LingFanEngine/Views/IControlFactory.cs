using Avalonia.Controls;
using LingFanEngine.Abstractions.Entities.UIs;

namespace LingFanEngine.Views;

/// <summary>
/// 控件工厂接口——将 UIElementEntity 转换为 Avalonia 控件，并应用布局与通用属性。
/// </summary>
public interface IControlFactory
{
    /// <summary>
    /// 将 UIElementEntity 转换为 Avalonia 控件
    /// </summary>
    Control? ConvertToControl(UIElementEntity element, double parentW, double parentH, string layoutMode);

    /// <summary>
    /// 应用布局属性（位置/尺寸/对齐/锚点等）
    /// </summary>
    void ApplyLayout(Control control, Dictionary<string, object> props, double pw, double ph, string layoutMode);

    /// <summary>
    /// 应用通用属性（padding/opacity/visible/enabled/zindex/cursor/transform/border 等）
    /// </summary>
    void ApplyCommonProps(Control control, Dictionary<string, object> props);

    /// <summary>清空绑定文本块列表（RebuildScene 开始时调用）</summary>
    void ClearBoundTextBlocks();

    /// <summary>刷新所有绑定表达式的 TextBlock（变量变化时重新求值）</summary>
    void RefreshBoundTextBlocks();
}
