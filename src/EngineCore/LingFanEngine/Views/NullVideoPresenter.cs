using Avalonia.Controls;

namespace LingFanEngine.Views;

/// <summary>
/// 空视频呈现器——桌面视频后端暂未实现期间的默认实现（全平台空操作）。
/// <para>保留 <see cref="IVideoPresenter"/> 接线契约：SceneView 照常 Attach/Detach/Update，
/// DSL video 命令照常解析执行（VideoManager 状态机仍工作），只是没有任何画面输出。</para>
/// <para>未来接入原生视频后端（VLC/FFmpeg 等）时，替换 DI 注册为真实呈现器即可，本类与上层零改动。</para>
/// </summary>
internal sealed class NullVideoPresenter : IVideoPresenter
{
    /// <inheritdoc/>
    public void Attach(Panel? sceneRoot, Grid? outerGrid) { }

    /// <inheritdoc/>
    public void Detach() { }

    /// <inheritdoc/>
    public void Update() { }
}
