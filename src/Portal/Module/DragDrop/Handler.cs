using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MinecraftLaunch.Components.Installer.Modpack;
using Portal.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Operations.Account;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Tab.Extensions;
using Tio.Avalonia.Standard.Tab.Gateway;
using Tio.Avalonia.Standard.Tab.Interface;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;
using Portal.Views.Pages.DownloadPages;
using Portal.Views.Pages.InstancePages;
using AuthServer = Portal.Core.Operations.Account.AuthServer;

namespace Portal.Module.DragDrop;

public class Handler
{
    public static async void Handle(DragEventArgs e, TioTabWindowBase window)
    {
        var data = e.DataTransfer;
        if (data.Contains(DataFormat.Text))
        {
            var text = data.TryGetText();
            if (TryParseAuthlibUrl(text, out var apiUrl, out var domain))
            {
                e.Handled = true;
                if (!string.IsNullOrEmpty(apiUrl) && !string.IsNullOrEmpty(domain))
                {
                    await HandleAuthServerUrlAsync(apiUrl, domain, window);
                }
            }
        }

        if (TryGetModpack(data, out var archivePath, out var source, out var suggestedInstanceId))
        {
            e.Handled = true;
            await ModpackDetailsPage.InstallLocalAsync(window, archivePath, source, suggestedInstanceId);
            return;
        }

        if (OperatingSystem.IsWindows() && TryGetBedrockPackage(data, out archivePath, out var inspection))
        {
            e.Handled = true;
            await BedrockPackageImportDialog.ImportAsync(window, archivePath, inspection);
        }
    }

    public static string GetMsg(DragEventArgs e)
    {
        var data = e.DataTransfer;
        DragDropEffects dropEffects = DragDropEffects.None;
        string? msg = null;
        e.Handled = true;
        if (!data.Contains(DataFormat.Text) && !data.Contains(DataFormat.Bitmap) &&
            !data.Contains(DataFormat.File)) return null;
        if (data.Contains(DataFormat.Text))
        {
            var text = data.TryGetText();
            if (TryParseAuthlibUrl(text, out var apiUrl, out var domain))
            {
                e.Handled = true;
                dropEffects = DragDropEffects.Link;
                msg = "识别到验证服务器";
            }
        }

        if (TryGetModpack(data, out _, out _, out _))
        {
            dropEffects = DragDropEffects.Copy;
            msg = "识别到整合包";
        }

        if (OperatingSystem.IsWindows() && TryGetBedrockPackage(data, out _, out _))
        {
            dropEffects = DragDropEffects.Copy;
            msg = "识别到基岩版包";
        }

        e.DragEffects = dropEffects;
        return msg ?? "不支持的拖放内容";
    }

    private static async Task HandleAuthServerUrlAsync(string url, string domain, TioTabWindowBase window)
    {
        var hostId = window.HostId;
        var options = new OverlayDialogOptions
        {
            Mode = DialogMode.None,
            Buttons = DialogButton.None,
            CanLightDismiss = false,
            CanDragMove = true,
            IsCloseButtonVisible = false,
            CanResize = false,
            VerticalAnchor = VerticalPosition.Top,
            VerticalOffset = 110
        };

        var result = await OverlayDialog
            .ShowCustomAsync<AuthServerDetected, AuthServerDetectedViewModel, AuthServerDetectedAction>(
                new AuthServerDetectedViewModel(url), hostId: hostId, options: options);

        switch (result)
        {
            case AuthServerDetectedAction.AddServer:
                await AddAuthServerAsync(url, domain, hostId, options, window);
                break;
            case AuthServerDetectedAction.Login:
                await LoginAccountAsync(url, hostId, options, window);
                break;
        }
    }

    private static async Task AddAuthServerAsync(string url, string domain, string? hostId,
        OverlayDialogOptions options, TioTabWindowBase window)
    {
        var existingServers = Data.ConfigEntry.AuthServers.ToArray();

        var vm = new AuthServerViewModel(existingServers)
        {
            ServerName = domain,
            ServerUrl = url
        };

        var result = await OverlayDialog
            .ShowCustomAsync<AuthServer, AuthServerViewModel, global::Portal.Core.Minecraft.Classes.AuthServer>(
                vm, hostId: hostId, options: options);

        if (result != null)
        {
            Data.ConfigEntry.AuthServers.Add(result);
            App.Method.SaveConfig();
            NotificationGateway.Notice(window.GetTopLevel(), "验证服务器已添加", NotificationType.Success);
        }
    }

    private static async Task LoginAccountAsync(string url, string? hostId, OverlayDialogOptions options,
        TioTabWindowBase window)
    {
        var result = await OverlayDialog.ShowCustomAsync<Yggdrasil, YggdrasilAccountViewModel, MinecraftAccount[]>(
            new YggdrasilAccountViewModel(Data.ConfigEntry.AuthServers, hostId) { ServerUrl = url }, hostId: hostId,
            options: options);

        if (result == null || result.Length == 0) return;

        foreach (var account in result)
        {
            if (account is null) continue;
            Data.ConfigEntry.MinecraftAccounts.Add(account);
        }

        Data.ConfigEntry.UsingMinecraftMinecraftAccount = result.LastOrDefault();
    }

    private static bool TryParseAuthlibUrl(string? input, out string? apiUrl, out string? domain)
    {
        apiUrl = null;
        domain = null;

        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim();
        const string PREFIX = "authlib-injector:yggdrasil-server:";
        if (!trimmed.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var encodedPart = trimmed.Substring(PREFIX.Length);
            var decoded = System.Net.WebUtility.UrlDecode(encodedPart);

            if (Uri.TryCreate(decoded, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                {
                    apiUrl = decoded;
                    domain = uri.Host;
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetModpack(IDataTransfer data, out string archivePath, out ModDetailsSource source,
        out string suggestedInstanceId)
    {
        archivePath = string.Empty;
        source = default;
        suggestedInstanceId = string.Empty;
        var files = data.TryGetFiles()?.OfType<IStorageFile>().ToArray();
        if (files is not [var file]) return false;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            if (extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                var entry = ModrinthModpackInstaller.ParseModpackInstallEntry(path);
                archivePath = path;
                source = ModDetailsSource.Modrinth;
                suggestedInstanceId = entry.Name;
                return true;
            }

            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var entry = CurseforgeModpackInstaller.ParseModpackInstallEntry(path);
                archivePath = path;
                source = ModDetailsSource.CurseForge;
                suggestedInstanceId = entry.Id;
                return true;
            }
        }
        catch (Exception) { }

        return false;
    }

    private static bool TryGetBedrockPackage(IDataTransfer data, out string archivePath,
        out BedrockPackageInspection inspection)
    {
        archivePath = string.Empty;
        inspection = null!;
        var files = data.TryGetFiles()?.OfType<IStorageFile>().ToArray();
        if (files is not [var file]) return false;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !BedrockPackageImportService.TryGetArchiveType(path, out _)) return false;

        try
        {
            inspection = new BedrockPackageImportService().Inspect(path);
            archivePath = path;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
