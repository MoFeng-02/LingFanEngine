using Avalonia.Controls;
using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Views;

/// <summary>
/// 对话框接口——供 SceneView 调用
/// <para>游戏可通过 IDialogBoxFactory 注册自定义实现，完全替换对话框 UI。</para>
/// <para>内置实现：DialogBox（底部条状打字机对话框）。</para>
/// </summary>
public interface IDialogBox
{
    /// <summary>打字机是否已完成</summary>
    bool IsComplete { get; }

    /// <summary>是否被 {w}/{p} 标签暂停</summary>
    bool IsPausedByTag { get; }

    /// <summary>设置对话文本和说话者</summary>
    void SetText(string text, string? speaker = null);

    /// <summary>每帧推进打字机</summary>
    void Advance(double deltaSeconds);

    /// <summary>跳到打字机末尾</summary>
    void SkipToEnd();

    /// <summary>隐藏对话框</summary>
    void Hide();

    /// <summary>重置 NVL 模式内部状态（场景切换或退出 NVL 模式时调用）</summary>
    void ResetNvlState();

    /// <summary>获取 Avalonia 控件（供 SceneView 添加到面板）</summary>
    Control AsControl();
}

/// <summary>
/// 对话框工厂接口——DI 注入，游戏可注册自定义实现
/// </summary>
public interface IDialogBoxFactory
{
    /// <summary>创建对话框实例</summary>
    IDialogBox Create(IStateContainer state);
}

/// <summary>
/// 默认对话框工厂——创建内置 DialogBox
/// <para>Phase 65: 改为 public，UI 层注册为 bottom 模板</para>
/// </summary>
public class DefaultDialogBoxFactory : IDialogBoxFactory
{
    public IDialogBox Create(IStateContainer state) => new DialogBox(state);
}

// ========== Phase 65: 模板注册系统 ==========

/// <summary>
/// 对话框模板注册表接口——UI 层注册模板工厂，SceneView 按名消费
/// <para>模板 = 独立 Control（实现 IDialogBox），委托 DialogEngine 共享核心逻辑。</para>
/// </summary>
public interface IDialogTemplateRegistry
{
    /// <summary>注册模板工厂。同名覆盖</summary>
    void Register(string name, IDialogBoxFactory factory);

    /// <summary>注销模板</summary>
    bool Unregister(string name);

    /// <summary>按名解析模板工厂。null 或未注册=返回 null</summary>
    IDialogBoxFactory? Resolve(string? name);

    /// <summary>获取全局默认模板工厂。UI 层必须设置至少一个</summary>
    IDialogBoxFactory? GetDefault();

    /// <summary>设置全局默认模板名</summary>
    void SetDefault(string name);

    /// <summary>已注册的所有模板名（供调试/SDK 列举）</summary>
    IReadOnlyCollection<string> RegisteredNames { get; }
}
