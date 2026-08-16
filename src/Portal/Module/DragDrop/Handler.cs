using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Portal.Bedrock.Standard.Interface;
using Portal.Classes;
using Portal.Const;
using Portal.Core.Classes;
using Portal.Core.Const;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Operations.Account;
using Portal.Core.Operations.OpenFile;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Initialize;
using Portal.Module.Initialize;
using Tio.Avalonia.Standard.Modules.DiskIO;
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
    
    
    
    private static readonly object IdentifyLock = new();

    private static string? _activeSignature;      
    private static string? _activeMessage;        
    private static DragDropEffects _activeEffects = DragDropEffects.None;

    private static string? _inFlightSignature;    

    public static async void Handle(DragEventArgs e, TioTabWindowBase window)
    {
        
        try
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

            if (TryGetMinecraftFolder(data, out var folderPath))
            {
                e.Handled = true;
                await HandleMinecraftFolderAsync(folderPath, window);
                return;
            }

            if (TryGetModpack(data, out var archivePath, out var source, out var suggestedInstanceId))
            {
                e.Handled = true;
                await ModpackDetailsPage.InstallLocalAsync(window, archivePath, source, suggestedInstanceId);
                return;
            }

            if (BedrockInstallationService.DefaultInstaller is not null &&
                TryGetBedrockPackage(data, out archivePath, out var inspection))
            {
                e.Handled = true;
                await BedrockPackageImportDialog.ImportAsync(window, archivePath, inspection);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"处理拖放内容失败：{ex}");
            NotificationGateway.Notice(window.GetTopLevel(), $"处理拖放内容失败：{ex.Message}",
                NotificationType.Error);
        }
    }

    public static string? GetMsg(DragEventArgs e)
    {
        var data = e.DataTransfer;
        if (!data.Contains(DataFormat.Text) && !data.Contains(DataFormat.Bitmap) &&
            !data.Contains(DataFormat.File))
            return null; 

        e.Handled = true;

        var hasFiles = data.Contains(DataFormat.File);
        
        
        var text = hasFiles ? null : SafeGetText(data);
        var paths = hasFiles ? SafeGetFilePaths(data) : null;

        var signature = BuildDragSignature(text, paths);
        if (signature is null) return null;

        lock (IdentifyLock)
        {
            
            if (signature == _activeSignature)
            {
                e.DragEffects = _activeEffects;
                return _activeMessage;
            }

            
            if (TryFastClassify(text, paths, out var fastMessage, out var fastEffects))
            {
                _activeSignature = signature;
                _activeMessage = fastMessage;
                _activeEffects = fastEffects;
                e.DragEffects = fastEffects;
                return fastMessage;
            }

            
            if (_inFlightSignature != signature)
            {
                _inFlightSignature = signature;
                var capturedText = text;
                var capturedPaths = paths;
                _ = Task.Run(() => IdentifyInBackground(signature, capturedText, capturedPaths));
            }

            
            e.DragEffects = hasFiles ? DragDropEffects.Copy : _activeEffects;
            return null;
        }
    }

        public static void ResetDragIdentification()
    {
        lock (IdentifyLock)
        {
            _activeSignature = null;
            _activeMessage = null;
            _activeEffects = DragDropEffects.None;
            _inFlightSignature = null;
        }
    }

        private static string? BuildDragSignature(string? text, string[]? paths)
    {
        if (paths is { Length: > 0 })
        {
            var normalized = paths
                .Select(NormalizePath)
                .Distinct()
                .OrderBy(path => path, StringComparer.Ordinal);
            return "file:" + string.Join("|", normalized);
        }

        return string.IsNullOrWhiteSpace(text) ? null : "text:" + text.Trim();
    }

    private static string NormalizePath(string path) =>
        path.Trim().Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

        private static bool TryFastClassify(string? text, string[]? paths,
        out string? message, out DragDropEffects effects)
    {
        if (paths is [var folderPath] && Directory.Exists(folderPath))
        {
            message = "识别到文件夹";
            effects = DragDropEffects.Copy;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(text) && TryParseAuthlibUrl(text, out _, out _))
        {
            message = "识别到验证服务器";
            effects = DragDropEffects.Link;
            return true;
        }

        message = null;
        effects = DragDropEffects.None;
        return false;
    }

    private static void IdentifyInBackground(string signature, string? text, string[]? paths)
    {
        try
        {
            DetectSource(text, paths, out var message, out var effects);
            lock (IdentifyLock)
            {
                
                if (_inFlightSignature != signature) return;
                _inFlightSignature = null;
                _activeSignature = signature;
                _activeMessage = message ?? "不支持的拖放内容";
                _activeEffects = effects;
            }
        }
        catch (Exception exception)
        {
            lock (IdentifyLock)
            {
                
                if (_inFlightSignature != signature) return;
                _inFlightSignature = null;
                _activeSignature = signature;
                _activeMessage = "不支持的拖放内容";
                _activeEffects = DragDropEffects.None;
            }
            Logger.Debug($"识别拖放内容失败：{signature}{Environment.NewLine}{exception}");
        }
    }

    private static string? SafeGetText(IDataTransfer data)
    {
        try
        {
            return data.TryGetText();
        }
        catch
        {
            return null;
        }
    }

    private static string[]? SafeGetFilePaths(IDataTransfer data)
    {
        try
        {
            return data.TryGetFiles()?.OfType<IStorageFile>()
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void DetectSource(string? text, string[]? paths, out string? message, out DragDropEffects effects)
    {
        message = null;
        effects = DragDropEffects.None;

        if (!string.IsNullOrWhiteSpace(text) && TryParseAuthlibUrl(text, out _, out _))
        {
            message = "识别到验证服务器";
            effects = DragDropEffects.Link;
        }

        if (paths is [var modpackPath] && ModpackSniffer.TrySniff(modpackPath, out _, out _))
        {
            message = "识别到整合包";
            effects = DragDropEffects.Copy;
        }

        if (BedrockInstallationService.DefaultInstaller is not null && paths is [var bedrockPath] && IsBedrockPackage(bedrockPath))
        {
            message = "识别到基岩版包";
            effects = DragDropEffects.Copy;
        }

        if (paths is [var folderPath] && Directory.Exists(folderPath))
        {
            message = "识别到文件夹";
            effects = DragDropEffects.Copy;
        }
    }

    private static bool IsBedrockPackage(string path)
    {
        if (!File.Exists(path) || !BedrockPackageImportService.TryGetArchiveType(path, out _)) return false;
        try
        {
            _ = new BedrockPackageImportService().Inspect(path);
            return true;
        }
        catch
        {
            return false;
        }
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
            ConfigSaver.SaveConfig();
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

    private static async Task HandleMinecraftFolderAsync(string folderPath, TioTabWindowBase window)
    {
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

        
        var viewModel = new NewMinecraftFolderViewModel(
            Data.ConfigEntry.MinecraftFolders.Select(x => x.FolderPath).ToList())
        {
            FolderPath = MinecraftFolderLayout.ResolveGameFolder(folderPath)
        };

        var result = await OverlayDialog
            .ShowCustomAsync<NewMinecraftFolder, NewMinecraftFolderViewModel, MinecraftFolderEntry>(
                viewModel, hostId: window.HostId, options: options);

        if (result == null) return;
        Data.ConfigEntry.MinecraftFolders.Add(result);
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

    public static bool TryGetModpack(IDataTransfer data, out string archivePath, out ModDetailsSource source,
        out string suggestedInstanceId)
    {
        archivePath = string.Empty;
        source = default;
        suggestedInstanceId = string.Empty;
        var files = data.TryGetFiles()?.OfType<IStorageFile>().ToArray();
        if (files is not [var file]) return false;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        if (!ModpackSniffer.TrySniff(path, out source, out var sniffedInstanceId)) return false;

        archivePath = path;
        suggestedInstanceId = sniffedInstanceId ?? string.Empty;
        return true;
    }

    private static bool TryGetMinecraftFolder(IDataTransfer data, out string folderPath)
    {
        folderPath = string.Empty;
        var items = data.TryGetFiles();
        if (items is not [var item]) return false;

        var path = item.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;

        folderPath = path;
        return true;
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
        catch (Exception exception)
        {
            Logger.Error($"检查拖放基岩版包失败：{path}", exception);
            return false;
        }
    }
}
