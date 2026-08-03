using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

internal static class BedrockResourceImport
{
    internal static bool Accepts(IDataTransfer data, params string[] extensions) => data.TryGetFiles()?.OfType<IStorageFile>()
        .Select(file => file.TryGetLocalPath()).OfType<string>().Any(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) == true;

    internal static async Task SelectAndImportAsync(UserControl owner, MinecraftInstance instance, string title,
        string resourceName, string[] extensions, string? userId, BedrockPackageContentType? expectedType, Func<Task> refresh)
    {
        if (TopLevel.GetTopLevel(owner) is not { } topLevel) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(resourceName) { Patterns = extensions.Select(extension => $"*{extension}").ToArray() }]
        });
        await ImportAsync(owner, files.Select(file => file.TryGetLocalPath()).OfType<string>(), instance, resourceName, extensions, userId, expectedType, false, refresh);
    }

    internal static async Task ImportDropAsync(UserControl owner, DragEventArgs e, MinecraftInstance instance,
        string resourceName, string[] extensions, string? userId, BedrockPackageContentType? expectedType, Func<Task> refresh)
    {
        var files = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().Select(file => file.TryGetLocalPath()).OfType<string>()
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray() ?? [];
        e.Handled = files.Length > 0;
        await ImportAsync(owner, files, instance, resourceName, extensions, userId, expectedType, true, refresh);
    }

    private static async Task ImportAsync(UserControl owner, IEnumerable<string> paths, MinecraftInstance instance,
        string resourceName, string[] extensions, string? userId, BedrockPackageContentType? expectedType, bool confirm, Func<Task> refresh)
    {
        var files = paths.Where(File.Exists).Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).ToArray();
        if (files.Length == 0 || TopLevel.GetTopLevel(owner) is not { } topLevel) return;
        Logger.Info($"[BedrockImport] Inspecting {files.Length} {resourceName} file(s) for {instance.InstanceName}.");
        var service = new BedrockPackageImportService();
        var validFiles = new List<(string Path, BedrockPackageInspection Inspection)>();
        var mismatched = 0;
        foreach (var file in files)
        {
            try
            {
                var inspection = service.Inspect(file);
                if (expectedType == null || inspection.Contents.Any(content => content.Type == expectedType))
                    validFiles.Add((file, inspection));
                else
                    mismatched++;
            }
            catch (Exception exception) { Logger.Warning($"[BedrockImport] Failed to inspect {file}: {exception}"); }
        }
        if (validFiles.Count == 0)
        {
            var message = mismatched > 0 ? $"所选文件不是{resourceName}" : "导入失败";
            NotificationGateway.Notice(topLevel, message, NotificationType.Error);
            return;
        }
        if (confirm)
        {
            var result = await OverlayDialog.ShowStandardAsync(new TextBlock { Margin = new Thickness(24),
                Text = $"确定要导入选中的 {validFiles.Count} 个{resourceName}吗？", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                null, owner.TryGetHostId(), new OverlayDialogOptions { Title = $"导入{resourceName}", Buttons = DialogButton.YesNo,
                    OverrideYesButtonText = "导入", OverrideNoButtonText = "取消", CanResize = false });
            if (result != DialogResult.Yes) return;
        }

        var succeeded = 0;
        foreach (var (file, inspection) in validFiles)
        {
            try
            {
                service.Import(file, inspection, instance, userId);
                succeeded++;
                Logger.Info($"[BedrockImport] Imported {resourceName} {file} into {instance.InstanceName}.");
            }
            catch (Exception exception) { Logger.Error(exception); }
        }
        if (succeeded > 0) await refresh();
        var allSucceeded = succeeded == validFiles.Count && mismatched == 0;
        NotificationGateway.Notice(topLevel, allSucceeded ? "导入成功" : succeeded == 0 ? "导入失败" : "部分导入成功",
            allSucceeded ? NotificationType.Success : succeeded == 0 ? NotificationType.Error : NotificationType.Warning);
        Logger.Info($"[BedrockImport] {resourceName} import completed for {instance.InstanceName}: {succeeded}/{validFiles.Count} valid file(s) succeeded, {mismatched} mismatched.");
    }
}
