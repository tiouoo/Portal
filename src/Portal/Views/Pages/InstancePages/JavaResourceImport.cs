using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Portal.Localization;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

internal static class JavaResourceImport
{
    internal static bool Accepts(IDataTransfer data, params string[] extensions)
    {
        return GetPaths(data)
            .Any(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    }

    internal static async Task SelectAndImportAsync(UserControl owner, string title, string destination,
        string resourceName, string[] extensions, bool saves, Func<Task> refresh)
    {
        if (TopLevel.GetTopLevel(owner) is not { } topLevel)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(resourceName)
                    { Patterns = extensions.Select(extension => $"*{extension}").ToArray() }
            ]
        });
        await ImportAsync(owner, files.Select(file => file.TryGetLocalPath()).OfType<string>(), destination,
            resourceName,
            extensions, saves, false, refresh);
    }

    internal static async Task ImportDropAsync(UserControl owner, DragEventArgs e, string destination,
        string resourceName, string[] extensions, bool saves, Func<Task> refresh)
    {
        var paths = GetPaths(e.DataTransfer)
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray();
        e.Handled = paths.Length > 0;
        await ImportAsync(owner, paths, destination, resourceName, extensions, saves, true, refresh);
    }

    private static async Task ImportAsync(UserControl owner, IEnumerable<string> paths, string destination,
        string resourceName, string[] extensions, bool saves, bool confirm, Func<Task> refresh)
    {
        await ImportAsync(TopLevel.GetTopLevel(owner), owner, paths, destination, resourceName, extensions, saves,
            confirm, refresh);
    }

    private static async Task ImportAsync(TopLevel? topLevel, UserControl? owner, IEnumerable<string> paths,
        string destination,
        string resourceName, string[] extensions, bool saves, bool confirm, Func<Task> refresh)
    {
        var files = paths.Where(path =>
                File.Exists(path) && extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (files.Length == 0 || topLevel == null)
            return;

        if (confirm)
        {
            var result = await OverlayDialog.ShowStandardAsync(new TextBlock
            {
                Margin = new Thickness(24),
                Text = string.Format(CommonLanguageManager.Instance.javaResourceImport_confirmImport.CurrentValue(),
                    files.Length, resourceName),
                TextWrapping = TextWrapping.Wrap
            }, null, owner?.TryGetHostId(), new OverlayDialogOptions
            {
                Title = string.Format(CommonLanguageManager.Instance.javaResourceImport_importTitle.CurrentValue(),
                    resourceName), Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.javaResourceImport_import.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(), CanResize = false
            });
            if (result != DialogResult.Yes)
                return;
        }

        var succeeded = 0;
        foreach (var file in files)
            try
            {
                if (saves)
                {
                    ImportSave(file, destination);
                }
                else
                {
                    Directory.CreateDirectory(destination);
                    File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
                }

                succeeded++;
            }
            catch (Exception)
            {
            }

        if (succeeded > 0)
            await refresh();
        var type = succeeded == files.Length ? NotificationType.Success :
            succeeded == 0 ? NotificationType.Error : NotificationType.Warning;
        var message = succeeded == files.Length
            ? CommonLanguageManager.Instance.javaResourceImport_importSuccess.CurrentValue()
            : succeeded == 0 ? CommonLanguageManager.Instance.javaResourceImport_importFailed.CurrentValue()
            : CommonLanguageManager.Instance.javaResourceImport_partialSuccess.CurrentValue();
        topLevel.Notice(message, type);
    }

    private static void ImportSave(string archivePath, string savesPath)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"portal-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryPath);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(temporaryPath, entry.FullName));
                if (!target.StartsWith(temporaryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        CommonLanguageManager.Instance.javaResourceImport_invalidSaveArchivePath.CurrentValue());
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, true);
                }
            }

            var root = File.Exists(Path.Combine(temporaryPath, "level.dat"))
                ? temporaryPath
                : Directory.EnumerateDirectories(temporaryPath)
                    .SingleOrDefault(path => File.Exists(Path.Combine(path, "level.dat")));
            if (root == null)
                throw new InvalidDataException(
                    CommonLanguageManager.Instance.javaResourceImport_saveNotFound.CurrentValue());
            Directory.CreateDirectory(savesPath);
            var name = Path.GetFileNameWithoutExtension(archivePath);
            var targetPath = Path.Combine(savesPath, name);

            for (var suffix = 2; Directory.Exists(targetPath); suffix++)
                targetPath = Path.Combine(savesPath, $"{name} ({suffix})");
            CopyDirectory(root, targetPath);
        }
        finally
        {
            if (Directory.Exists(temporaryPath)) Directory.Delete(temporaryPath, true);
        }
    }

    private static IEnumerable<string> GetPaths(IDataTransfer data)
    {
        return data.TryGetFiles()?
            .OfType<IStorageFile>().Select(file => file.TryGetLocalPath()).OfType<string>().Where(File.Exists) ?? [];
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }
}