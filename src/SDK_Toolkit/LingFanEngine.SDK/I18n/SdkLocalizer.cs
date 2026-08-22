using System;
using System.Globalization;

namespace LingFanEngine.SDK.I18n;

/// <summary>
/// SDK 界面本地化访问器：从强类型 <see cref="AppLang"/> 资源按当前 UI 文化取文本。
/// <para>语言切换调用 <see cref="SetCulture"/>（例如 zh-Hans / en-US / ja-JP）。</para>
/// </summary>
public static class SdkLocalizer
{
    /// <summary>语言切换后触发（供 ViewModel / View 刷新本地化文案）。</summary>
    public static event Action? CultureChanged;

    /// <summary>设置 UI 语言并刷新资源文化的缓存。仅在语言实际变化时触发 <see cref="CultureChanged"/>。</summary>
    public static void SetCulture(string culture)
    {
        var @new = CultureInfo.GetCultureInfo(culture);
        if (Equals(AppLang.Culture, @new))
            return;
        AppLang.Culture = @new;
        CultureChanged?.Invoke();
    }

    /// <summary>当前 UI 文化（未显式设置时归为系统当前 UI 文化）。</summary>
    public static CultureInfo Culture => AppLang.Culture ?? CultureInfo.CurrentUICulture;

    /// <summary>取本地化文本；可带 <c>{0}</c> 占位参数。缺失键原样返回键名便于定位。</summary>
    public static string Loc(string key, params object?[] args)
    {
        var value = AppLang.ResourceManager.GetString(key, AppLang.Culture);
        if (string.IsNullOrEmpty(value))
            return key;
        return args.Length == 0 ? value : string.Format(CultureInfo.InvariantCulture, value, args);
    }
}