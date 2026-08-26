using Iridium.Minecraft;
using Iridium.Models.Minecraft;
using Iridium.Interfaces;

namespace Portal.Core.Minecraft;

public static class IridiumEntryHelper
{
    public static IMinecraftLayout GetLayout(MinecraftContext context) => context.Layout;

    public static string GetMinecraftRoot(MinecraftContext context) => GetLayout(context).GetInstanceRoot(context.Entry);

    public static string GetNativesDirectory(MinecraftContext context) => GetLayout(context).GetNativesDirectory(context.Entry);

    public static string GetAssetsDirectory(MinecraftContext context) => GetLayout(context).GetAssetsRoot(context.Entry);

    public static string GetWorkingPath(MinecraftContext context, bool isEnableIndependency) =>
        isEnableIndependency ? GetLayout(context).GetGameDirectory(context.Entry) : GetMinecraftRoot(context);

    public static int GetAppropriateJavaVersion(MinecraftEntry entry) => entry.RequiredJavaVersion ?? 8;
}