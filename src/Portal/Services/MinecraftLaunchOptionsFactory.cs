using Portal.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;

namespace Portal.Services;

public static class MinecraftLaunchOptionsFactory
{
    public static MinecraftLaunchOptions Create(Action<MinecraftLogSession>? openLog = null) => new()
    {
        Account = Data.ConfigEntry.UsingMinecraftMinecraftAccount,
        JavaRuntimes = Data.ConfigEntry.JavaRuntimes,
        DefaultJavaRuntime = Data.ConfigEntry.DefaultJavaRuntime,
        WindowWidth = Data.ConfigEntry.MinecraftWindowWidth,
        WindowHeight = Data.ConfigEntry.MinecraftWindowHeight,
        MaxMemory = Data.ConfigEntry.MinecraftMaxMemory,
        AccountRefreshed = UpdateMicrosoftAccount,
        OpenLog = openLog
    };

    private static void UpdateMicrosoftAccount(MinecraftAccount original, MinecraftAccount refreshed)
    {
        var accounts = Data.ConfigEntry.MinecraftAccounts;
        var index = accounts.IndexOf(original);
        if (index >= 0)
            accounts[index] = refreshed;
        Data.ConfigEntry.UsingMinecraftMinecraftAccount = refreshed;
    }
}
