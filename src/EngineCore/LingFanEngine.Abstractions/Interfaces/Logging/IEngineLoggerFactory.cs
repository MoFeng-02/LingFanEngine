namespace LingFanEngine.Abstractions.Interfaces.Logging;

/// <summary>
/// 引擎日志工厂——按分类创建 logger 实例。
/// <para>每个服务通过工厂创建自己的 logger，携带分类名。</para>
/// </summary>
public interface IEngineLoggerFactory
{
    /// <summary>创建指定分类的 logger</summary>
    IEngineLogger Create(string category);
}
