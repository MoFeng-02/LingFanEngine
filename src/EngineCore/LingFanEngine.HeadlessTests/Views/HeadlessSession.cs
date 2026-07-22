using Avalonia;
using Avalonia.Headless;

namespace LingFanEngine.Tests.Views;

/// <summary>
/// 全程序集唯一共享的 headless 会话。
/// <para>Avalonia 的 <see cref="HeadlessUnitTestSession"/> 在 <c>StartNew</c> 时会经
/// <c>EnsureIsolatedApplication → UseHeadless → Initialize</c> 操作全局 <c>AvaloniaLocator</c> 与 render loop。
/// 若同一测试进程内创建多个会话（跨测试类、或每测试一个又 dispose），后创建的会话会复用已被污染或随前会话
/// 死亡而失效的全局 dispatcher / 渲染循环，触发随机的
/// <c>InvalidOperationException: The calling thread cannot access this object because a different thread owns it</c>
/// （结果随测试执行顺序漂移，隔离跑全绿、全套跑偶发挂）。</para>
/// <para>故所有 headless 视图测试统一复用本助手暴露的唯一会话实例（整个进程仅 <c>StartNew</c> 一次），
/// 根除多会话全局状态污染。会话随进程退出自然回收，不显式 Dispose。</para>
/// <para>注意：本助手刻意<b>不</b>用 [ModuleInitializer] 在程序集加载时强行初始化——那会把 headless 全局状态
/// 常驻整个进程，反而污染无宿主测试（如缓存增长类测试）。保持惰性首次访问创建即可。</para>
/// </summary>
internal static class HeadlessSession
{
    private sealed class HeadlessApp : Application { }

    private static readonly System.Lazy<HeadlessUnitTestSession> Shared =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    /// <summary>全程序集唯一 headless 会话；首次访问时惰性创建（仅一次）。</summary>
    public static HeadlessUnitTestSession Instance => Shared.Value;
}
