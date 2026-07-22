using LingFanEngine.Abstractions.Interfaces.Media;

namespace LingFanEngine.Views;

/// <summary>
/// 无后端平台的视频播放器占位实现（Browser/WASM 或视频不可用环境）。
/// <para>Control 返回 null，VideoPresenter 据此跳过可视树挂载；视频由宿主外部处理
/// （如 WASM 的 HTML &lt;video&gt; 直出）。宿主也可自行实现 IVideoPlayer 覆盖此默认。</para>
/// </summary>
internal sealed class NullVideoPlayer : IVideoPlayer
{
    public object? Control => null;
}
