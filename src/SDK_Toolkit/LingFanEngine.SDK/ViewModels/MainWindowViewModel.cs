using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFToolkit.Routing.Core.Interfaces;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>主窗口 ViewModel</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IRouter _router;

    [ObservableProperty]
    private string _currentRoute = "/project";

    [ObservableProperty]
    private string _title = "灵泛引擎 SDK";

    public MainWindowViewModel(IRouter router)
    {
        _router = router;
        _router.Navigated += (_, args) =>
        {
            CurrentRoute = args.To?.Entity.RoutePath ?? "/project";
        };
    }

    /// <summary>导航到指定路由</summary>
    [RelayCommand]
    private async Task NavigateAsync(string path)
    {
        await _router.NavigateAsync(path);
    }

    /// <summary>返回上一页</summary>
    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (_router.CanGoBack)
            await _router.GoBackAsync();
    }
}
