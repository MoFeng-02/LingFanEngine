using CommunityToolkit.Mvvm.ComponentModel;

namespace _LingFanEngineTemplateTitle_.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";
}
