using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Core.Const;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Localization;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Core.Minecraft.Services;

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
                              ?? throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.javaAutoInstall_noJavaForPlatform.CurrentValue(), majorVersion));
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
                Text = CommonLanguageManager.Instance.javaAutoInstall_confirmText.CurrentValue(),
                TextWrapping = TextWrapping.Wrap
            }, null, topLevel.TryGetHostId(), new OverlayDialogOptions
            {
                Title = string.Format(CommonLanguageManager.Instance.javaAutoInstall_title.CurrentValue(), majorVersion),
                Buttons = DialogButton.YesNo,
                OverrideYesButtonText = CommonLanguageManager.Instance.javaAutoInstall_yesButton.CurrentValue(),
                OverrideNoButtonText = CommonLanguageManager.Instance.common_cancel.CurrentValue(),
                CanLightDismiss = false, CanResize = false
            });
            return result == DialogResult.Yes;
        });
    }
}