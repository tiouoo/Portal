using Portal.Core.Minecraft.Classes;
using Portal.Views.Pages;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Services;

/// <summary>
/// Coordinates destructive instance operations across windows and tabs.
/// </summary>
public static class InstanceDeletionCoordinator
{
    private static readonly HashSet<string> DeletingPaths = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryBegin(MinecraftInstance instance)
    {
        lock (DeletingPaths)
            return DeletingPaths.Add(NormalizePath(instance.InstanceFolderPath));
    }

    public static void Complete(MinecraftInstance instance)
    {
        lock (DeletingPaths)
            DeletingPaths.Remove(NormalizePath(instance.InstanceFolderPath));
    }

    public static bool IsDeleting(MinecraftInstance instance)
    {
        lock (DeletingPaths)
            return DeletingPaths.Contains(NormalizePath(instance.InstanceFolderPath));
    }

    public static void CloseRelatedPages(MinecraftInstance instance)
    {
        foreach (var tab in TioTabWindowBase.AllWindows
                     .SelectMany(window => window.Tabs)
                     .Where(tab => IsRelatedPage(tab.Content, instance))
                     .ToArray())
        {
            // A destructive operation must not be blocked by unsaved edits in a page being removed.
            tab.CloseImmediately();
        }
    }

    private static bool IsRelatedPage(object page, MinecraftInstance instance) => page switch
    {
        InstanceDetailPage detail => Matches(detail.ViewModel.Instance, instance),
        MinecraftLogPage { Instance: { } logInstance } => Matches(logInstance, instance),
        _ => false
    };

    private static bool Matches(MinecraftInstance left, MinecraftInstance right) =>
        string.Equals(NormalizePath(left.InstanceFolderPath), NormalizePath(right.InstanceFolderPath),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
