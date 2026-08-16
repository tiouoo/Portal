using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Core.Const;
using Portal.Core.Minecraft.Instance.Java;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Core.Services;

public static class JavaAutoInstallCoordinator
{
    private static readonly SemaphoreSlim InstallLock = new(1, 1);

    public static async Task<JavaRuntimeEntry?> EnsureAsync(int majorVersion,
        JavaInstallProgressHandler? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing =
            Data.ConfigEntry.JavaRuntimes.FirstOrDefault(x =>
                x.MajorVersion == majorVersion && File.Exists(x.JavaPath));
        if (existing is not null) return existing;
        var approved = await ConfirmAsync(majorVersion);
        if (!approved) return null;

        await InstallLock.WaitAsync(cancellationToken);
        try
        {
            existing = Data.ConfigEntry.JavaRuntimes.FirstOrDefault(x =>
                x.MajorVersion == majorVersion && File.Exists(x.JavaPath));
            if (existing is not null) return existing;
            var runtime = await JavaDistributionService.InstallMojangAsync(majorVersion, ConfigPath.JavaRuntimesPath,
                progress, cancellationToken);
            if (runtime is null)
            {
                var version = await JavaDistributionService.GetFastestVersionAsync(majorVersion, cancellationToken)
                              ?? throw new InvalidOperationException($"没有找到适用于当前平台的 Java {majorVersion}。 ");
                runtime = await JavaDistributionService.InstallAsync(version, ConfigPath.JavaRuntimesPath,
                    ConfigPath.TempFolderPath, progress, cancellationToken);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Data.ConfigEntry.JavaRuntimes.Contains(runtime)) Data.ConfigEntry.JavaRuntimes.Add(runtime);
                Data.ConfigEntry.DefaultJavaRuntime ??= runtime;
            });
            return runtime;
        }
        finally
        {
            InstallLock.Release();
        }
    }

    private static async Task<bool> ConfirmAsync(int majorVersion)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var topLevel = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow;
            if (topLevel is null) return false;
            var result = await OverlayDialog.ShowStandardAsync(new TextBlock
            {
                Margin = new Thickness(24),
                Text = "暂未发现适配版本 Java，是否自动安装？",
                TextWrapping = TextWrapping.Wrap
            }, null, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = $"需要 Java {majorVersion}", Buttons = DialogButton.YesNo,
                OverrideYesButtonText = "自动安装", OverrideNoButtonText = "取消",
                CanLightDismiss = false, CanResize = false
            });
            return result == DialogResult.Yes;
        });
    }
}