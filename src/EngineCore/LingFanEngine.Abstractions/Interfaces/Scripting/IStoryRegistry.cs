using LingFanEngine.Abstractions.Interfaces.Core;

namespace LingFanEngine.Abstractions.Interfaces.Scripting;

/// <summary>
/// 故事懒加载注册表接口
/// <para>启动时扫描 Stories 目录建立 "场景名 → 文件路径" 映射。</para>
/// <para>实际文件在第一次 navigate/scene 指令时才读取编译。</para>
/// </summary>
public interface IStoryRegistry
{
    /// <summary>已注册的场景数</summary>
    int RegisteredCount { get; }

    /// <summary>已加载的场景数</summary>
    int LoadedCount { get; }

    /// <summary>扫描目录，建立 场景名→文件路径 映射（不加载内容）</summary>
    void Scan();

    /// <summary>按需加载场景——查找文件、读取、编译、注册 SceneEntity</summary>
    bool LoadScene(string sceneName);

    /// <summary>获取编译结果（命令列表 + 标签索引）</summary>
    (IReadOnlyList<ICommand>? Commands, IReadOnlyDictionary<string, int>? Labels)
        GetCompiledResult(string sceneName);

    /// <summary>按文件路径获取编译结果</summary>
    (IReadOnlyList<ICommand>? Commands, IReadOnlyDictionary<string, int>? Labels)
        GetCompiledResultByFile(string filePath);

    /// <summary>获取所有已知的 story 文件路径（去重）</summary>
    IEnumerable<string> GetAllStoryFiles();

    /// <summary>按文件路径加载场景和流程命令</summary>
    bool LoadSceneFromFile(string filePath);

    /// <summary>通过 label 名查找对应的文件路径</summary>
    string? FindFileByLabel(string label);

    /// <summary>确保 label 所在的文件已被加载并编译</summary>
    bool EnsureLabelLoaded(string label);

    /// <summary>扫描完成后，注册所有已知文件的 define 到状态容器</summary>
    void RegisterAllDefines();

    /// <summary>检查场景是否可加载</summary>
    bool CanLoad(string sceneName);

    /// <summary>
    /// 热重载：重新加载指定文件的所有场景和编译结果
    /// <para>清除该文件的已加载标记和编译缓存，重新读取、编译、注册。</para>
    /// <para>返回该文件包含的所有场景名列表（用于 UI 刷新判断）。</para>
    /// </summary>
    List<string> ReloadFile(string filePath);
}
