namespace LingFanEngine.Abstractions.Entities.UIs;

/// <summary>
/// UI 元素实体
/// <para>场景中的最小组成单元，描述一个具体的控件或交互组件。</para>
/// </summary>
public class UIElementEntity : BaseEntity
{
    /// <summary>
    /// 元素类型："Text", "Button", "Image", "Video", "Panel", "MiniGame", "Live2D"
    /// </summary>
    public required string ElementType { get; set; }

    /// <summary>
    /// 是自定义控件
    /// </summary>
    public bool InCustom { get; set; } = false;

    /// <summary>
    /// 自定义元素
    /// </summary>
    public object? CustomElement { get; set; }

    /// <summary>
    /// 元素的具体属性（文本内容、颜色、尺寸、路径等）
    /// </summary>
    public Dictionary<string, object> Properties { get; set; } = [];
    /// <summary>
    /// 子元素列表，支持嵌套（如 Panel 内包含多个子控件）
    /// </summary>
    public List<UIElementEntity> Children { get; set; } = [];
    /// <summary>
    /// 渲染/排列顺序，数值越小越靠前
    /// </summary>
    public int Order { get; set; }
}
