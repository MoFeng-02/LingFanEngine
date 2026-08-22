using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using LingFanEngine.SDK.ViewModels;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.Views.Pages;

/// <summary>
/// 资源管理页（.axaml）——布局在 AssetManagerPage.axaml，编译绑定 + 令牌配色；
/// 拖拽导入由 code-behind 挂接（需要 Input 事件）。
/// </summary>
public partial class AssetManagerPage : UserControl, INavigationAware
{
    public AssetManagerPage()
    {
        InitializeComponent();
        AttachDragDrop();
    }

    // ===== INavigationAware =====

    public void OnNavigated(Dictionary<string, object?>? parameters)
    {
        // 导航到资源页时自动扫描
        if (DataContext is AssetManagerViewModel vm && vm.Assets.Count == 0)
            vm.ScanAssetsCommand.Execute(null);
    }

    public void OnNavigatingFrom() { }
    public void OnNavigatedFrom() { }

    // ===== 拖拽导入（P2-5） =====

    private void AttachDragDrop()
    {
        DragDrop.SetAllowDrop(AssetList, true);
        AssetList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AssetList.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>拖拽悬停——文件才允许放</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>拖拽释放——导入文件</summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files == null) return;

        var filePaths = files
            .Select(f => f.Path.LocalPath)
            .Where(System.IO.File.Exists)
            .ToArray();

        if (filePaths.Length > 0 && DataContext is AssetManagerViewModel vm)
            _ = vm.ImportFilesCommand.ExecuteAsync(filePaths);
    }
}