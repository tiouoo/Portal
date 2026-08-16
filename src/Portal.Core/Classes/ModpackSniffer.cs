using System.IO;
using MinecraftLaunch.Components.Installer.Modpack;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Classes;

public static class ModpackSniffer
{
    public static bool TrySniff(string archivePath, out ModDetailsSource source, out string? suggestedInstanceId)
    {
        source = default;
        suggestedInstanceId = null;
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath)) return false;

        try
        {
            var entry = ModrinthModpackInstaller.ParseModpackInstallEntry(archivePath);
            source = ModDetailsSource.Modrinth;
            suggestedInstanceId = entry.Name;
            return true;
        }
        catch (Exception exception)
        {
            Logger.Debug($"按 Modrinth 格式识别整合包失败：{archivePath}{Environment.NewLine}{exception}");
        }

        try
        {
            var entry = CurseforgeModpackInstaller.ParseModpackInstallEntry(archivePath);
            source = ModDetailsSource.CurseForge;
            suggestedInstanceId = entry.Id;
            return true;
        }
        catch (Exception exception)
        {
            Logger.Debug($"按 CurseForge 格式识别整合包失败：{archivePath}{Environment.NewLine}{exception}");
        }

        return false;
    }
}
