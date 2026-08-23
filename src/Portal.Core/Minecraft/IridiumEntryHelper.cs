using Iridium.Interfaces.Minecraft;
using Iridium.Models.Minecraft;
using Iridium.Parsers.Launch;

namespace Portal.Core.Minecraft;

public static class IridiumEntryHelper
{
    public static IMinecraftLayout GetLayout(MinecraftEntry entry) =>
        entry.Layout ?? new DefaultMinecraftLayoutFactory().Create(entry.Format);

    public static string GetMinecraftRoot(MinecraftEntry entry) => GetLayout(entry).GetInstanceRoot(entry);

    public static string GetNativesDirectory(MinecraftEntry entry) => GetLayout(entry).GetNativesDirectory(entry);

    public static string GetAssetsDirectory(MinecraftEntry entry) => GetLayout(entry).GetAssetsRoot(entry);

    public static string GetWorkingPath(MinecraftEntry entry, bool isEnableIndependency) =>
        isEnableIndependency ? GetLayout(entry).GetGameDirectory(entry) : GetMinecraftRoot(entry);

    public static int GetAppropriateJavaVersion(MinecraftEntry entry) => entry.RequiredJavaVersion ?? 8;
}
