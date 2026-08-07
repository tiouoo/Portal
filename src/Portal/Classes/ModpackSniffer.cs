using System.IO;
using MinecraftLaunch.Components.Installer.Modpack;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Classes;

/// <summary>
/// 按压缩包内容识别整合包格式，而不是依赖文件扩展名：
/// Modrinth 包常被下载/改名保存为 .zip，此时不能按扩展名判定为 CurseForge。
/// 依次尝试 Modrinth（modrinth.index.json）与 CurseForge（manifest.json），先解析成功者胜。
/// </summary>
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
