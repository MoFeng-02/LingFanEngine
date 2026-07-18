namespace LingFanEngine.Abstractions.Interfaces.Saves;

/// <summary>
/// 系统配置服务接口
/// <para>管理语言、音量、帧率等不随用户存档变动的配置。</para>
/// <para>启动时自动加载，修改时自动保存。</para>
/// </summary>
public interface IConfigService
{
    /// <summary>获取配置值</summary>
    T? Get<T>(string key);

    /// <summary>设置配置值（自动保存）</summary>
    void Set<T>(string key, T value);

    /// <summary>从磁盘加载配置</summary>
    void Load();

    /// <summary>保存配置到磁盘</summary>
    void Save();
}
