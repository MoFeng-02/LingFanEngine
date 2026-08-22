using System.Collections.Generic;
using Avalonia.Controls;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 设置页（.axaml 试点）——布局在 SettingsPage.axaml，编译绑定（x:DataType）+ 令牌配色。
/// <para>提供公共无参构造供 Avalonia 运行时 loader（消 AVLN3001）；Router 创建后通过 DataContext 注入 VM。</para>
/// </summary>
public partial class SettingsPage : UserControl, INavigationAware
{
    private bool _configComboInitialized;

    public SettingsPage()
    {
        InitializeComponent();
    }

    // ===== INavigationAware =====

    public void OnNavigated(Dictionary<string, object?>? parameters) => InitBuildConfigCombo();
    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    /// <summary>默认配置下拉：Debug/Release ↔ DefaultBuildConfig 字符串（幂等，仅在 DataContext 就绪后初始化一次）。</summary>
    private void InitBuildConfigCombo()
    {
        if (_configComboInitialized || DataContext is not SettingsViewModel vm)
            return;

        BuildConfigCombo.Items.Add("Debug");
        BuildConfigCombo.Items.Add("Release");
        BuildConfigCombo.SelectedIndex = vm.DefaultBuildConfig == "Debug" ? 0 : 1;
        BuildConfigCombo.SelectionChanged += (_, _) =>
            vm.DefaultBuildConfig = BuildConfigCombo.SelectedIndex == 0 ? "Debug" : "Release";
        _configComboInitialized = true;
    }
}