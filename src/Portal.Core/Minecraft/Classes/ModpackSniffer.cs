using MinecraftLaunch.Components.Installer.Modpack;
using Portal.Core.Minecraft.Models;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Classes;

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
            Logger.Debug(string.Format(LogLanguageManager.Instance.modpack_sniffModrinthFailed.CurrentValue(), archivePath, Environment.NewLine, exception));
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
            Logger.Debug(string.Format(LogLanguageManager.Instance.modpack_sniffCurseForgeFailed.CurrentValue(), archivePath, Environment.NewLine, exception));
        }

        return false;
    }
}