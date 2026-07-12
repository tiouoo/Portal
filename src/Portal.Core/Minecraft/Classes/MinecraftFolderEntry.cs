using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using TioUi.Common.Extensions;

namespace Portal.Core.Minecraft.Classes;

public partial class MinecraftFolderEntry : ObservableObject, IEquatable<MinecraftFolderEntry>
{
    [ObservableProperty] public partial string FolderName { get; set; }
    [ObservableProperty] public partial string FolderPath { get; set; }
    [ObservableProperty] public partial bool EnableIndependentVersion { get; set; } = true;

    public MinecraftFolderEntry()
    {
        PropertyChanged += (_, _) =>
        {
            Events.RaiseCoreSaveSettings();
        };
    }

    public void OpenFolder(object parameter)
    {
        var topLevel = (parameter as Control)?.GetTopLevel();
        topLevel?.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(FolderPath));
    }

    public bool Equals(MinecraftFolderEntry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(FolderPath, other.FolderPath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MinecraftFolderEntry);
    }

    public override int GetHashCode()
    {
        return FolderPath != null
            ? StringComparer.OrdinalIgnoreCase.GetHashCode(FolderPath)
            : 0;
    }

    public static bool operator ==(MinecraftFolderEntry? left, MinecraftFolderEntry? right)
    {
        if (left is null) return right is null;
        return left.Equals(right);
    }

    public static bool operator !=(MinecraftFolderEntry? left, MinecraftFolderEntry? right)
    {
        return !(left == right);
    }
}