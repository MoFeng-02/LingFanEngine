using CommunityToolkit.Mvvm.ComponentModel;

namespace LingFanEngine.SDK.Models;

/// <summary>可加密文件类型勾选项</summary>
public partial class FileTypeCheckItem : ObservableObject
{
    /// <summary>文件扩展名（如 ".story"）</summary>
    public string Extension { get; }

    /// <summary>显示名称（如 "故事文件 (.story)"）</summary>
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isChecked;

    public FileTypeCheckItem(string extension, bool isChecked = true)
    {
        Extension = extension;
        DisplayName = extension switch
        {
            ".story" => "故事文件",
            ".json" => "JSON 配置",
            ".png" => "PNG 图片",
            ".jpg" => "JPG 图片",
            ".jpeg" => "JPEG 图片",
            ".gif" => "GIF 图片",
            ".webp" => "WebP 图片",
            ".mp3" => "MP3 音频",
            ".ogg" => "OGG 音频",
            ".wav" => "WAV 音频",
            ".mp4" => "MP4 视频",
            ".webm" => "WebM 视频",
            ".mkv" => "MKV 视频",
            _ => extension,
        };
        _isChecked = isChecked;
    }
}
