namespace LingFanEngine.Abstractions.Interfaces.Entry;

/// <summary>
/// 运行时翻译服务接口
/// <para>按需加载翻译文件，纯原文直译。</para>
/// <para>启动时只扫描 Lang/ 目录注册可用语言，不加载任何文件。</para>
/// </summary>
public interface II18nService
{
    /// <summary>切换语言——清除缓存，下次 Translate() 按需加载</summary>
    void SwitchLanguage(string lang);

    /// <summary>原文→译文翻译</summary>
    string Translate(string original);
}
