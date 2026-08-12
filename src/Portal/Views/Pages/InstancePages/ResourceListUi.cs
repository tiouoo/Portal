using System.ComponentModel;
using System.Globalization;

namespace Portal.Views.Pages.InstancePages;

/// <summary>资源管理列表页的排序方式。每个页面的排序状态互相独立。</summary>
public enum ResourceSortMode
{
    /// <summary>文件名称（默认）。</summary>
    FileName = 0,

    /// <summary>资源名称。</summary>
    Name = 1,

    /// <summary>加入时间（文件的修改时间）。</summary>
    LastWriteTime = 2,

    /// <summary>文件大小（文件夹则为文件夹总大小）。</summary>
    FileSize = 3
}

/// <summary>资源管理列表页的筛选方式。</summary>
public enum ResourceFilterMode
{
    /// <summary>全部。</summary>
    All = 0,

    /// <summary>已启用。</summary>
    Enabled = 1,

    /// <summary>已禁用。</summary>
    Disabled = 2,

    /// <summary>重复。</summary>
    Duplicates = 3
}

/// <summary>筛选下拉框中的一项。文案（含数量）可实时更新，且对象身份保持不变，避免选中状态被重置。</summary>
public sealed class ResourceFilterOption(string label) : INotifyPropertyChanged
{
    private string _label = label;

    public string Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => _label;
}

/// <summary>资源管理列表页共用的排序、筛选与大小统计辅助方法。</summary>
public static class ResourceListUi
{
    /// <summary>排序下拉框中的选项文本。</summary>
    public static string[] SortOptions { get; } = ["文件名称", "资源名称", "加入时间", "文件大小"];

    /// <summary>把字节数格式化为 "12MiB" 形式的大小文本。</summary>
    public static string FormatSize(long size)
    {
        if (size < 0)
            size = 0;
        if (size < 1024)
            return $"{size} B";

        var value = (double)size;
        foreach (var unit in new[] { "KiB", "MiB", "GiB", "TiB" })
        {
            value /= 1024.0;
            if (value < 1024.0 || unit == "TiB")
                return $"{value.ToString("0.##", CultureInfo.InvariantCulture)}{unit}";
        }

        return $"{size} B";
    }

    /// <summary>计算文件夹的总大小（字节）。</summary>
    public static long GetFolderSize(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return 0;
            return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>生成筛选下拉框的带数量选项文本，如 "全部(121)"。</summary>
    public static string BuildFilterLabel(string name, int count) => $"{name}({count})";
}
