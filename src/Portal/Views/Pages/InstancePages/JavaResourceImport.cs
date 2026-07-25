using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

internal static class JavaResourceImport
{
    internal static bool Accepts(IDataTransfer data, params string[] extensions) =>
        GetPaths(data).Any(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

    internal static async Task SelectAndImportAsync(UserControl owner, string title, string destination,
        string resourceName, string[] extensions, bool saves, Func<Task> refresh)
    {
        if (TopLevel.GetTopLevel(owner) is not { } topLevel)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(resourceName) { Patterns = extensions.Select(extension => $"*{extension}").ToArray() }]
        });
        await ImportAsync(owner, files.Select(file => file.TryGetLocalPath()).OfType<string>(), destination, resourceName,
            extensions, saves, false, refresh);
    }

    internal static async Task ImportDropAsync(UserControl owner, DragEventArgs e, string destination,
        string resourceName, string[] extensions, bool saves, Func<Task> refresh)
    {
        var paths = GetPaths(e.DataTransfer).Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray();
        e.Handled = paths.Length > 0;
        await ImportAsync(owner, paths, destination, resourceName, extensions, saves, true, refresh);
    }

    private static async Task ImportAsync(UserControl owner, IEnumerable<string> paths, string destination,
        string resourceName, string[] extensions, bool saves, bool confirm, Func<Task> refresh)
    {
        await ImportAsync(TopLevel.GetTopLevel(owner), owner, paths, destination, resourceName, extensions, saves, confirm, refresh);
    }

    private static async Task ImportAsync(TopLevel? topLevel, UserControl? owner, IEnumerable<string> paths, string destination,
        string resourceName, string[] extensions, bool saves, bool confirm, Func<Task> refresh)
    {
        var files = paths.Where(path => File.Exists(path) && extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray();
        if (files.Length == 0 || topLevel == null)
            return;

        if (confirm)
        {
            var result = await OverlayDialog.ShowStandardAsync(new TextBlock
            {
                Margin = new Thickness(24),
                Text = $"确定要导入选中的 {files.Length} 个{resourceName}吗？",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }, null, owner?.TryGetHostId(), new OverlayDialogOptions
            {
                Title = $"导入{resourceName}", Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "导入", OverrideNoButtonText = "取消", CanResize = false
            });
            if (result != DialogResult.Yes)
                return;
        }

        var succeeded = 0;
        foreach (var file in files)
        {
            try
            {
                if (saves)
                    ImportSave(file, destination);
                else
                {
                    Directory.CreateDirectory(destination);
                    File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
                }
                succeeded++;
            }
            catch (Exception)
            {
                // Continue importing remaining files so one locked file does not block the selection.
            }
        }

        if (succeeded > 0)
            await refresh();
        var type = succeeded == files.Length ? NotificationType.Success : succeeded == 0 ? NotificationType.Error : NotificationType.Warning;
        var message = succeeded == files.Length ? "导入成功" : succeeded == 0 ? "导入失败" : "部分导入成功";
        NotificationGateway.Notice(topLevel, message, type);
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
                    throw new InvalidDataException("存档压缩包包含无效路径。");
                if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, true);
                }
            }

            var root = File.Exists(Path.Combine(temporaryPath, "level.dat")) ? temporaryPath :
                Directory.EnumerateDirectories(temporaryPath).SingleOrDefault(path => File.Exists(Path.Combine(path, "level.dat")));
            if (root == null)
                throw new InvalidDataException("压缩包中未找到有效的 Minecraft 存档。");
            Directory.CreateDirectory(savesPath);
            var name = Path.GetFileNameWithoutExtension(archivePath);
            var targetPath = Path.Combine(savesPath, name);
            if (Directory.Exists(targetPath)) Directory.Delete(targetPath, true);
            CopyDirectory(root, targetPath);
        }
        finally
        {
            if (Directory.Exists(temporaryPath)) Directory.Delete(temporaryPath, true);
        }
    }

    private static IEnumerable<string> GetPaths(IDataTransfer data) => data.TryGetFiles()?
        .OfType<IStorageFile>().Select(file => file.TryGetLocalPath()).OfType<string>().Where(File.Exists) ?? [];

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }

}
