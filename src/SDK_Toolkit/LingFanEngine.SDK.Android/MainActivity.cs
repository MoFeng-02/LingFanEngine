using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace LingFanEngine.SDK.Android;

[Activity(
    Label = "LingFanEngine.SDK.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
