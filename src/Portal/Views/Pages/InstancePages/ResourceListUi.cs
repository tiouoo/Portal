using System.ComponentModel;
using System.Globalization;

namespace Portal.Views.Pages.InstancePages;

public enum ResourceSortMode
{
    FileName = 0,

    Name = 1,

    LastWriteTime = 2,

    FileSize = 3
}

public enum ResourceFilterMode
{
    All = 0,

    Enabled = 1,

    Disabled = 2,

    Duplicates = 3
}

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

    public override string ToString()
    {
        return _label;
    }
}

public static class ResourceListUi
{
    public static string[] SortOptions { get; } = ["文件名称", "资源名称", "加入时间", "文件大小"];

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

    public static string BuildFilterLabel(string name, int count)
    {
        return $"{name}({count})";
    }
}