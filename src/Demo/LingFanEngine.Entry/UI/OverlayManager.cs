using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using LingFanEngine.Abstractions;
using LingFanEngine.Abstractions.Interfaces.Core;
using LingFanEngine.Abstractions.Interfaces.Saves;
using LingFanEngine.Services.Core;

namespace LingFanEngine.Entry.UI;

/// <summary>
/// 覆盖层管理器——管理所有 Demo 层 UI 面板的显示/隐藏和事件路由
/// <para>作为 Grid 覆盖在 SceneView 之上，统一管理存档/设置/历史/快捷菜单等面板。</para>
/// <para>支持快捷键：ESC 关闭面板、右键打开快捷菜单、H 查看历史。</para>
/// </summary>
public class OverlayManager : Grid
{
    private readonly SaveLoadPanel _saveLoadPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly HistoryPanel _historyPanel;
    private readonly QuickMenuPanel _quickMenuPanel;
    private readonly GalleryPanel _galleryPanel;
    private readonly DebugConsolePanel _debugConsolePanel;
    private readonly IStateContainer _state;

    public OverlayManager(IStateContainer state, ISaveService? saveService, IGameController? controller)
    {
        _state = state;

        // 创建各面板
        _saveLoadPanel = new SaveLoadPanel(state, saveService, controller);
        _settingsPanel = new SettingsPanel(state);
        _historyPanel = new HistoryPanel(state);
        _quickMenuPanel = new QuickMenuPanel(state, controller);
        _galleryPanel = new GalleryPanel(state, controller);
        _debugConsolePanel = new DebugConsolePanel(state, controller);

        // 面板关闭事件
        _saveLoadPanel.Closed += OnPanelClosed;
        _settingsPanel.Closed += OnPanelClosed;
        _historyPanel.Closed += OnPanelClosed;
        _quickMenuPanel.Closed += OnPanelClosed;
        _galleryPanel.Closed += OnPanelClosed;
        _debugConsolePanel.Closed += OnPanelClosed;

        // 快捷菜单项选择
        _quickMenuPanel.MenuItemSelected += OnMenuItemSelected;

        // 添加到覆盖层（后添加的在上层）
        Children.Add(_saveLoadPanel);
        Children.Add(_settingsPanel);
        Children.Add(_historyPanel);
        Children.Add(_galleryPanel);
        Children.Add(_debugConsolePanel);
        Children.Add(_quickMenuPanel);

        // 右键打开快捷菜单
        PointerReleased += OnPointerReleased;

        // 注意：不订阅 KeyDown——键盘事件由 MainWindow 直接调用 HandleKeyDown 转发，
        // 避免使用 RaiseEvent 导致路由事件冒泡回 Window 造成无限递归 (StackOverflow)
    }

    /// <summary>面板关闭时同步状态</summary>
    private void OnPanelClosed()
    {
        // 关闭历史面板时同步可见性状态
        _state.Set(StateKeys.History.Visible, false);
    }

    /// <summary>快捷菜单项选择处理</summary>
    private void OnMenuItemSelected(string action)
    {
        switch (action)
        {
            case "save":
                HideAll();
                _saveLoadPanel.Show(saveMode: true);
                break;
            case "load":
                HideAll();
                _saveLoadPanel.Show(saveMode: false);
                break;
            case "settings":
                HideAll();
                _settingsPanel.Show();
                break;
            case "history":
                HideAll();
                _state.Set(StateKeys.History.Visible, true);
                _historyPanel.Show();
                break;
            case "gallery":
                HideAll();
                _state.Set(StateKeys.Gallery.Visible, true);
                _galleryPanel.Show();
                break;
            case "debug":
                HideAll();
                _state.Set(StateKeys.Debug.Visible, true);
                _debugConsolePanel.Show();
                break;
        }
    }

    /// <summary>右键释放事件——打开快捷菜单</summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            // 如果有面板打开，则关闭所有面板
            if (AnyPanelVisible())
            {
                HideAll();
                return;
            }

            HideAll();
            _quickMenuPanel.Show();
            e.Handled = true;
        }
    }

    /// <summary>键盘快捷键——由 MainWindow.OnKeyDown 直接调用，不通过 RaiseEvent 转发</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (AnyPanelVisible())
                {
                    HideAll();
                    e.Handled = true;
                }
                break;

            case Key.H:
                if (!AnyPanelVisible())
                {
                    _state.Set(StateKeys.History.Visible, true);
                    _historyPanel.Show();
                    e.Handled = true;
                }
                break;

            case Key.G:
                if (!AnyPanelVisible())
                {
                    _state.Set(StateKeys.Gallery.Visible, true);
                    _galleryPanel.Show();
                    e.Handled = true;
                }
                break;

            case Key.F12:
                HideAll();
                _state.Set(StateKeys.Debug.Visible, true);
                _debugConsolePanel.Show();
                e.Handled = true;
                break;

            case Key.S when e.KeyModifiers == KeyModifiers.Control:
                HideAll();
                _saveLoadPanel.Show(saveMode: true);
                e.Handled = true;
                break;

            case Key.L when e.KeyModifiers == KeyModifiers.Control:
                HideAll();
                _saveLoadPanel.Show(saveMode: false);
                e.Handled = true;
                break;
        }
    }

    /// <summary>隐藏所有面板</summary>
    public void HideAll()
    {
        _saveLoadPanel.Hide();
        _settingsPanel.Hide();
        _historyPanel.Hide();
        _quickMenuPanel.Hide();
        _galleryPanel.Hide();
        _debugConsolePanel.Hide();
        _state.Set(StateKeys.History.Visible, false);
        _state.Set(StateKeys.Gallery.Visible, false);
        _state.Set(StateKeys.Debug.Visible, false);
    }

    /// <summary>检查是否有面板可见</summary>
    public bool AnyPanelVisible() =>
        _saveLoadPanel.IsVisible || _settingsPanel.IsVisible ||
        _historyPanel.IsVisible || _quickMenuPanel.IsVisible ||
        _galleryPanel.IsVisible || _debugConsolePanel.IsVisible;

    /// <summary>每帧更新（由 GameLoop.OnFrame 调用）</summary>
    public void Update(double delta)
    {
        // 历史面板可见时刷新内容
        if (_historyPanel.IsVisible)
            _historyPanel.RefreshHistory();
        // 调试面板可见时刷新日志
        if (_debugConsolePanel.IsVisible)
            _debugConsolePanel.RefreshLogs();
    }
}
