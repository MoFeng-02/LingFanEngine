namespace LingFanEngine.Abstractions.Entities.Medias;

/// <summary>
/// 媒体实体
/// <para>描述图片、视频、音频等资源</para>
/// </summary>
public class MediaEntity : BaseEntity
{
    /// <summary>
    /// 媒体类型："Image", "Video", "Audio", "Live2D"
    /// </summary>
    public required string MediaType { get; set; }
    /// <summary>
    /// 资源文件路径（相对于 DLC 包或全局资源目录）
    /// </summary>
    public required string Path { get; set; }
    /// <summary>
    /// 显示宽度（可选），如果是视频和图片资源的话，包括live2d，默认就是全屏，不给值的话
    /// </summary>
    public int? Width { get; set; }
    /// <summary>
    /// 显示高度（可选），如果是视频和图片资源的话，包括live2d，默认就是全屏，不给值的话
    /// </summary>
    public int? Height { get; set; }
    /// <summary>
    /// 是否循环播放（视频/音频），默认 false
    /// </summary>
    public bool Loop { get; set; }
    /// <summary>
    /// 音量（0.0 - 1.0），默认 1.0
    /// </summary>
    public float Volume { get; set; }
}
