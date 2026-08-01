using Microsoft.Win32;
using Windows.Management.Deployment;
using Portal.Bedrock.Standard.Manifest;

namespace Portal.Bedrock;

internal static class BedrockWindowsPrerequisites
{
    public static void Validate(BedrockInstanceConfig config)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("基岩版启动需要 Windows 10 2004 (19041) 或更高版本。");

        if (config.BuildType == BedrockBuildType.UWP && !IsDeveloperModeEnabled())
            throw new InvalidOperationException(
                "启动解包 UWP 基岩版需要启用 Windows 开发人员模式。请打开“设置 > 系统 > 开发者选项”后重试。");

        var packages = new PackageManager().FindPackagesForUser(string.Empty,
            "Microsoft.GamingServices_8wekyb3d8bbwe");
        if (!packages.Any())
            throw new InvalidOperationException(
                "未检测到 Microsoft Gaming Services。请先从 Microsoft Store 安装“游戏服务”后重试。");
    }

    private static bool IsDeveloperModeEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
        return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int value && value == 1;
    }
}
