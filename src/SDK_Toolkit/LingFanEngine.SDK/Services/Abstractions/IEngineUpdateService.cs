using System;
using System.Threading;
using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>
/// 引擎 DLL 独立更新服务——从 GitHub Release 拉取 manifest，下载并应用引擎 DLL 更新。
/// <para>HTTP 调用通过 IHttpClientFactory 获取 HttpClient，避免套接字耗尽（DNS 变更/端口耗尽）。</para>
/// <para>更新目标分两类：</para>
/// <list type="bullet">
/// <item><see cref="UpdateProjectAsync"/>：更新用户项目的 DLL/（4 个 DLL 全部热替换，先杀游戏进程）。</item>
/// <item><see cref="UpdateSdkCacheAsync"/>：更新 SDK 自带 DLL 缓存（LingFanEngine.dll 热替换；
/// 其余 3 个被 SDK 进程锁定，写 pending，重启后由 <see cref="ApplyPendingUpdatesAsync"/> 应用）。</item>
/// </list>
/// </summary>
public interface IEngineUpdateService
{
    /// <summary>
    /// SDK 引擎缓存的已知最新版本（读缓存 engine.lock.json 的 engineVersion；兜底回落 SDK 自身 DLL/ 的 LingFanEngine 元数据）。
    /// <para>注意：这不是「当前引擎版本」的真相来源——真相在最终项目的 engine.lock.json。
    /// 此值仅作「未打开任何项目时」的兜底展示值，以及填充/校验引擎缓存时使用。</para>
    /// </summary>
    string CurrentEngineVersion { get; }

    /// <summary>
    /// 读取最终项目 DLL/ 内的真实引擎版本（从 LingFanEngine.dll 的 AssemblyName 元数据读取，不加载程序集）。
    /// <para>版本真相在最终项目，不在 SDK 的种子缓存。未找到/损坏时返回空字符串。</para>
    /// </summary>
    /// <param name="projectRootDir">用户项目根目录（DLL/ 的父目录，即 .lfproj 所在目录）。</param>
    string GetProjectEngineVersion(string projectRootDir);

    /// <summary>
    /// 读取指定项目根的 engine.lock.json（版本真相）。不存在/损坏时返回 null。
    /// </summary>
    /// <param name="projectRootDir">用户项目根目录。</param>
    EngineLockFile? ReadProjectLock(string projectRootDir);

    /// <summary>
    /// 为新建项目播种引擎 DLL：确保引擎缓存齐全（离线也能用），从缓存复制全部 4 个 DLL 到项目 DLL/，
    /// 并写入 engine.lock.json（版本取自缓存）。供 TemplateService 建项目后调用。
    /// <para>缓存为空（离线且未安装引擎 DLL）时抛 InvalidOperationException，提示需联网或重装 SDK。
    /// 离线时缓存由安装包自带全部 4 个 DLL，因此始终 4 个齐全，不存在「只 3 个」的降级。</para>
    /// </summary>
    /// <param name="projectRootDir">新建项目的根目录。</param>
    Task SeedNewProjectEngineAsync(string projectRootDir);

    /// <summary>
    /// 检查 GitHub Release 是否有更新。版本比对以 <paramref name="currentVersions"/> 为本地基线（逐 DLL）：
    /// 清单中任一 DLL 的版本 &gt; 本地对应 DLL 版本，即视为有更新（相等或更低返回 null，绝不降级）。
    /// <para>逐 DLL 比对可覆盖「只更新了某个依赖（如 Pidgin）而 LingFanEngine 版本号未变」的情形，
    /// 避免误判已最新而漏掉其它 DLL 的更新。</para>
    /// </summary>
    /// <param name="currentVersions">本地逐 DLL 版本表（key=DLL 文件名）；为 null 时取 SDK 自身 DLL/ 的 4 个版本。
    /// 各更新目标应传入自身 DLL 版本表（如用户项目传其 DLL/ 内版本表），避免误降级或重复覆盖。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>远端清单（有更新时）；已是最新或失败时返回 null。</returns>
    Task<EngineUpdateManifest?> CheckForUpdatesAsync(Dictionary<string, string>? currentVersions = null, CancellationToken ct = default);

    /// <summary>
    /// 下载并应用更新到指定用户项目的 DLL/ 目录。
    /// <para>用户项目的 DLL 不被 SDK 进程加载，4 个 DLL 全部可热替换；会先杀掉从项目目录运行的游戏进程以防文件锁。</para>
    /// </summary>
    /// <param name="projectRootDir">用户项目根目录（DLL/ 的父目录）。</param>
    /// <param name="progress">进度回调（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<EngineUpdateResult> UpdateProjectAsync(
        string projectRootDir, IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 下载并更新 SDK 自带的 DLL 缓存（AppContext.BaseDirectory/DLL/）。
    /// <para>LingFanEngine.dll 不被 SDK 引用，可热替换；Abstractions/DslCore/Pidgin 被 SDK 进程锁定，
    /// 写入 pending 暂存，需重启 SDK 后由 <see cref="ApplyPendingUpdatesAsync"/> 应用。</para>
    /// </summary>
    /// <param name="progress">进度回调（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<EngineUpdateResult> UpdateSdkCacheAsync(
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// 应用上次未完成的 pending 更新（SDK 启动时调用）。
    /// <para>逐个尝试覆盖目标 DLL；仍被锁定的条目保留 pending，下次启动重试。</para>
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    Task ApplyPendingUpdatesAsync(CancellationToken ct = default);
}
