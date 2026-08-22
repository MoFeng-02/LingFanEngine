using CommunityToolkit.Mvvm.ComponentModel;
using LingFanEngine.SDK.I18n;

namespace LingFanEngine.SDK.ViewModels;

/// <summary>
/// ViewModel 基类（CommunityToolkit.Mvvm）
/// <para>已提供 SetProperty / [ObservableProperty] / [RelayCommand] 源生成。</para>
/// <para>语言切换（<see cref="SdkLocalizer.CultureChanged"/>）时自动对全部本地化绑定发出
/// <c>PropertyChanged(全量)</c>，使 .axaml 中 <c>{Binding 本地化属性}</c> 即时刷新。</para>
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        // 单次订阅：语言切换即重读全部本地化静态文案（VM 为应用级单例，无需退订）。
        SdkLocalizer.CultureChanged += Relocalize;
    }

    private void Relocalize() => OnPropertyChanged((string?)null);
}
