using System.Collections.Generic;
using Avalonia.Controls;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 构建发布页（.axaml）——布局/全部绑定在 BuildPage.axaml；
/// 仅"全选/全不选"与密钥分片 +/- 落在 code-behind（集合逐项操作）。
/// </summary>
public partial class BuildPage : UserControl, INavigationAware
{
    public BuildPage()
    {
        InitializeComponent();
        WireSelectionButtons();
    }

    // ===== INavigationAware =====

    public void OnNavigated(Dictionary<string, object?>? parameters) { }
    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    private void WireSelectionButtons()
    {
        SelectAllBtn.Click += (_, _) => SetAllChecked(true);
        SelectNoneBtn.Click += (_, _) => SetAllChecked(false);
        MinusBtn.Click += (_, _) => AdjustShard(-1);
        PlusBtn.Click += (_, _) => AdjustShard(+1);
    }

    private void SetAllChecked(bool value)
    {
        if (DataContext is BuildViewModel vm)
            foreach (var item in vm.EncryptFileTypes)
                item.IsChecked = value; // IsChecked 为 ObservableProperty，绑定自动刷新
    }

    private void AdjustShard(int delta)
    {
        if (DataContext is BuildViewModel vm)
        {
            var v = vm.KeyShardCount + delta;
            if (v >= 2 && v <= 16)
                vm.KeyShardCount = v;
        }
    }
}