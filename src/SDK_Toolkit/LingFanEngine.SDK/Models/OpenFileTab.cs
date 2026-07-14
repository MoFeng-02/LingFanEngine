using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LingFanEngine.SDK.Models;

/// <summary>P1-5: 打开的文件标签页模型</summary>
public class OpenFileTab : INotifyPropertyChanged
{
    private bool _isDirty;
    private bool _isActive;
    private string _filePath = "";
    private string _fileName = "";

    /// <summary>文件完整路径</summary>
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath != value)
            {
                _filePath = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>文件名（显示用）</summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName != value)
            {
                _fileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    /// <summary>文件内容</summary>
    public string Content { get; set; } = "";

    /// <summary>是否有未保存修改</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    /// <summary>是否为当前活动标签</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>光标行号（切换标签时保存/恢复）</summary>
    public int CaretLine { get; set; } = 1;

    /// <summary>光标列号（切换标签时保存/恢复）</summary>
    public int CaretColumn { get; set; } = 1;

    /// <summary>垂直滚动偏移（切换标签时保存/恢复）</summary>
    public double ScrollOffset { get; set; }

    /// <summary>显示名称（含脏标记）</summary>
    public string DisplayName => IsDirty ? $"● {FileName}" : FileName;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
