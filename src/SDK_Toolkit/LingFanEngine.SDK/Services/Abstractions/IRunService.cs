using System.Threading.Tasks;
using LingFanEngine.SDK.Models;

namespace LingFanEngine.SDK.Services.Abstractions;

/// <summary>游戏运行服务——从 SDK 启动/停止用户已构建的游戏。</summary>
public interface IRunService
{
    /// <summary>
    /// 启动游戏：定位 <c>publish/{RID}/</c> 下的可执行文件并运行。
    /// <para>若尚未构建（找不到可执行文件），自动构建当前操作系统对应的平台后再启动。</para>
    /// </summary>
    /// <param name="project">当前项目配置</param>
    /// <param name="progress">进度回调（用于构建日志）</param>
    Task<RunResult> LaunchAsync(ProjectConfig project, IProgress<string>? progress = null);

    /// <summary>
    /// 停止游戏：杀掉从项目目录运行的游戏进程（防止文件锁）。
    /// <para>与 <see cref="IPublishService"/> 构建前的杀进程逻辑一致，供用户手动中断运行中的游戏。</para>
    /// </summary>
    Task StopAsync(ProjectConfig project);
}
