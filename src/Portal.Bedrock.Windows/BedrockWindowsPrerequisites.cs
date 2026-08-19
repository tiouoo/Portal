using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Portal.Bedrock.Core;
using Portal.Bedrock.Core.Windows;
using Microsoft.Win32;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Windows.Management.Deployment;
using Portal.Localization;

namespace Portal.Bedrock;

internal static class BedrockWindowsPrerequisites
{
    private const string GameInputProductGuid = "64d0ccb1-329e-d507-0886-47e53d59ae21";
    private static readonly TimeSpan FailedRetryCooldown = TimeSpan.FromHours(24);

    private static string StateFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cc.tiouo.Portal", "Bedrock", "prerequisites.json");

    public static void Validate(BedrockInstanceConfig config)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException(CommonLanguageManager.Instance.bedrockLaunch_windowsVersionRequired.CurrentValue());

        if (config.BuildType == BedrockBuildType.UWP && !IsDeveloperModeEnabled())
            throw new InvalidOperationException(
                CommonLanguageManager.Instance.bedrockLaunch_developerModeRequired.CurrentValue());

        var packages = new PackageManager().FindPackagesForUser(string.Empty,
            "Microsoft.GamingServices_8wekyb3d8bbwe");
        if (!packages.Any())
            throw new InvalidOperationException(
                CommonLanguageManager.Instance.bedrockLaunch_gamingServicesMissing.CurrentValue());
    }

    public static async Task EnsureDependenciesAsync(string instancePath, BedrockBuildType buildType,
        Action<string, double?>? progress = null,
        Action<string, BedrockLogLevel>? log = null,
        CancellationToken cancellationToken = default)
    {
        var state = LoadState();
        log?.Invoke(string.Format(LogLanguageManager.Instance.bedrockLaunch_checkingDependencies.CurrentValue(), instancePath, buildType), BedrockLogLevel.Information);
        var architecture = RuntimeInformation.OSArchitecture;
        var core = new BedrockWindowsCore();
        var (hasVcUwp, hasVcWin32) = core.IsHasVCRuntime(architecture);

        if (!hasVcWin32)
        {
            progress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_checkingSystemEnvironment.CurrentValue(), null);
            await EnsureVcWin32Async(architecture, progress, log, cancellationToken).ConfigureAwait(false);
        }

        if (buildType == BedrockBuildType.UWP && !hasVcUwp)
        {
            if (state.VcUwp)
            {
                state.VcUwpFailedAt = null;
            }
            else if (IsRetryAllowed(state.VcUwpFailedAt))
            {
                await EnsureVcUwpAsync(architecture, state, progress, log, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_uwpVcSkipped.CurrentValue(), BedrockLogLevel.Warning);
            }
        }
        else if (hasVcUwp)
        {
            state.VcUwp = true;
            state.VcUwpFailedAt = null;
        }

        if (!IsGameInputInstalled())
        {
            if (state.GameInput)
            {
                state.GameInputFailedAt = null;
            }
            else if (IsRetryAllowed(state.GameInputFailedAt))
            {
                await EnsureGameInputAsync(instancePath, state, progress, log, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_gameInputSkipped.CurrentValue(), BedrockLogLevel.Warning);
            }
        }
        else
        {
            state.GameInput = true;
            state.GameInputFailedAt = null;
        }

        SaveState(state);
        log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_dependenciesCheckComplete.CurrentValue(), BedrockLogLevel.Information);
        progress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_systemEnvironmentChecked.CurrentValue(), 100);
    }

    private static async Task EnsureVcWin32Async(Architecture architecture, Action<string, double?>? progress,
        Action<string, BedrockLogLevel>? log, CancellationToken cancellationToken)
    {
        log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_vcMissingInstalling.CurrentValue(), BedrockLogLevel.Information);
        var tempFolder = Path.Combine(Path.GetTempPath(), "Portal", "VCRuntime");
        Directory.CreateDirectory(tempFolder);
        var fileName = architecture switch
        {
            Architecture.X86 => "vc_redist.x86.exe",
            Architecture.Arm64 => "vc_redist.arm64.exe",
            _ => "vc_redist.x64.exe"
        };
        var vcPath = Path.Combine(tempFolder, fileName);
        var exitCode = await InstallWin32RuntimeAsync(vcPath, architecture, progress, cancellationToken)
            .ConfigureAwait(false);
        if (exitCode is not (0 or 3010 or 1638))
            throw new InvalidOperationException(
                string.Format(CommonLanguageManager.Instance.bedrockLaunch_vcInstallFailedManual.CurrentValue(), exitCode));
        log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_vcInstalled.CurrentValue(), BedrockLogLevel.Information);
    }

    private static async Task EnsureVcUwpAsync(Architecture architecture, PrerequisitesState state,
        Action<string, double?>? progress, Action<string, BedrockLogLevel>? log, CancellationToken cancellationToken)
    {
        log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_uwpVcMissingInstalling.CurrentValue(), BedrockLogLevel.Information);
        var appxUrl = architecture switch
        {
            Architecture.X86 => VCRuntimeHelper.VCUri.Uwpx86,
            Architecture.Arm64 => VCRuntimeHelper.VCUri.Uwparm64,
            _ => VCRuntimeHelper.VCUri.Uwpx64
        };
        var tempFolder = Path.Combine(Path.GetTempPath(), "Portal", "VCRuntime");
        Directory.CreateDirectory(tempFolder);
        var appxPath = Path.Combine(tempFolder, "Microsoft.VCLibs.140.00.appx");
        try
        {
            await DownloadFileAsync(appxUrl, appxPath, cancellationToken,
                    p => progress?.Invoke(string.Format(CommonLanguageManager.Instance.bedrockLaunch_downloadingUwpVcProgress.CurrentValue(), p), p))
                .ConfigureAwait(false);
            progress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_installingUwpVc.CurrentValue(), null);
            var result = await UwpRegister.AddAppxAsync(new DeploymentOptionsConfig
            {
                PackagePath = appxPath,
                DeploymentOptions = DeploymentOptions.ForceApplicationShutdown
            }).ConfigureAwait(false);
            if (result.IsRegistered && HasVcUwpRuntime())
            {
                state.VcUwp = true;
                state.VcUwpFailedAt = null;
                log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_uwpVcInstalled.CurrentValue(), BedrockLogLevel.Information);
            }
            else
            {
                state.VcUwpFailedAt = DateTime.UtcNow;
                log?.Invoke(string.Format(LogLanguageManager.Instance.bedrockLaunch_uwpVcInstallFailed.CurrentValue(), result.ErrorText), BedrockLogLevel.Warning);
            }
        }
        catch (Exception exception)
        {
            state.VcUwpFailedAt = DateTime.UtcNow;
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_uwpVcInstallException.CurrentValue(), Environment.NewLine, exception));
            log?.Invoke(string.Format(LogLanguageManager.Instance.bedrockLaunch_uwpVcInstallExceptionShort.CurrentValue(), exception), BedrockLogLevel.Warning);
        }
    }

    private static async Task EnsureGameInputAsync(string instancePath, PrerequisitesState state,
        Action<string, double?>? progress, Action<string, BedrockLogLevel>? log, CancellationToken cancellationToken)
    {
        log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_gameInputMissingInstalling.CurrentValue(), BedrockLogLevel.Information);
        progress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_installingGameInput.CurrentValue(), null);
        var bundled = Path.Combine(instancePath, "Installers", "GameInputRedist.msi");
        if (File.Exists(bundled))
        {
            var exitCode = await RunElevatedAsync("msiexec.exe", $"/i \"{bundled}\" /qn /norestart")
                .ConfigureAwait(false);
            if (exitCode is 0 or 3010 or 3019)
            {
                MarkGameInputInstalled(state);
                log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_gameInputInstalled.CurrentValue(), BedrockLogLevel.Information);
                return;
            }

            log?.Invoke(string.Format(LogLanguageManager.Instance.bedrockLaunch_bundledGameInputFailed.CurrentValue(), exitCode),
                BedrockLogLevel.Warning);
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), "Portal", "GameInput");
        Directory.CreateDirectory(tempFolder);
        var msiPath = Path.Combine(tempFolder, "GameInputRedist.msi");
        try
        {
            await DownloadFileAsync(VCRuntimeHelper.VCUri.GameInputRedist, msiPath, cancellationToken,
                    p => progress?.Invoke(string.Format(CommonLanguageManager.Instance.bedrockLaunch_downloadingGameInputProgress.CurrentValue(), p), p))
                .ConfigureAwait(false);
            progress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_installingGameInput.CurrentValue(), null);
            var exitCode = await RunElevatedAsync("msiexec.exe", $"/i \"{msiPath}\" /qn /norestart")
                .ConfigureAwait(false);
            if (exitCode is 0 or 3010 or 3019)
            {
                MarkGameInputInstalled(state);
                log?.Invoke(LogLanguageManager.Instance.bedrockLaunch_gameInputInstalled.CurrentValue(), BedrockLogLevel.Information);
            }
            else
            {
                state.GameInputFailedAt = DateTime.UtcNow;
                log?.Invoke(string.Format(LogLanguageManager.Instance.bedrockLaunch_gameInputInstallFailed.CurrentValue(), exitCode), BedrockLogLevel.Warning);
            }
        }
        catch (Exception exception)
        {
            state.GameInputFailedAt = DateTime.UtcNow;
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_gameInputInstallException.CurrentValue(), Environment.NewLine, exception));
            log?.Invoke(string.Format(LogLanguageManager.Instance.bedrockLaunch_gameInputInstallExceptionShort.CurrentValue(), exception), BedrockLogLevel.Warning);
        }
    }

    private static void MarkGameInputInstalled(PrerequisitesState state)
    {
        state.GameInput = true;
        state.GameInputFailedAt = null;
    }

    private static async Task<int> InstallWin32RuntimeAsync(string vcPath, Architecture architecture,
        Action<string, double?>? progress, CancellationToken cancellationToken)
    {
        var urls = architecture switch
        {
            Architecture.X86 => new[]
            {
                "https://aka.ms/vs/17/release/vc_redist.x86.exe",
                VCRuntimeHelper.VCUri.Win32x86
            },
            Architecture.Arm64 => new[]
            {
                "https://aka.ms/vs/17/release/vc_redist.arm64.exe",
                VCRuntimeHelper.VCUri.Win32arm64
            },
            _ => new[]
            {
                "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                VCRuntimeHelper.VCUri.Win32x64
            }
        };

        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                await DownloadFileAsync(url, vcPath, cancellationToken,
                        p => progress?.Invoke(string.Format(CommonLanguageManager.Instance.bedrockLaunch_downloadingVcProgress.CurrentValue(), p), p))
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lastError = exception;
                Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_vcDownloadFailed.CurrentValue(), url, Environment.NewLine, exception));
                continue;
            }

            progress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_installingVc.CurrentValue(), null);
            return await RunElevatedAsync(vcPath, "/install /quiet /norestart").ConfigureAwait(false);
        }

        throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_vcDownloadFailedManual.CurrentValue(), lastError);
    }

    private static bool IsGameInputInstalled()
    {
        try
        {
            if (MsiHelper.IsMsiProductInstalledByGuid(GameInputProductGuid)) return true;
            var state = MsiQueryProductState("{" + GameInputProductGuid + "}");
            return state is 7 or 9;
        }
        catch (Exception exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_checkGameInputStatusFailed.CurrentValue(), Environment.NewLine, exception));
            return false;
        }
    }

    private static bool HasVcUwpRuntime()
    {
        try
        {
            return new PackageManager().FindPackagesForUser(string.Empty)
                .Any(package => package.Id.Name.Contains("Microsoft.VCLibs.140", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_checkUwpVcStatusFailed.CurrentValue(), Environment.NewLine, exception));
            return false;
        }
    }

    private static bool IsRetryAllowed(DateTime? failedAt) =>
        failedAt is null || DateTime.UtcNow - failedAt.Value > FailedRetryCooldown;

    private static async Task<int> RunElevatedAsync(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null) return -1;
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 1223 or 5)
        {
            throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockLaunch_adminPermissionCancelled.CurrentValue());
        }
    }

    private static async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken,
        Action<double>? onProgress = null)
    {
        using var client = new HttpClient();
        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrockLaunch_downloadingDependency.CurrentValue(), url, path));
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloaded += read;
            if (onProgress is not null && total is > 0)
                onProgress(downloaded * 100d / total.Value);
        }
    }

    private static PrerequisitesState LoadState()
    {
        try
        {
            if (!File.Exists(StateFilePath)) return new PrerequisitesState();
            return JsonSerializer.Deserialize<PrerequisitesState>(File.ReadAllText(StateFilePath))
                   ?? new PrerequisitesState();
        }
        catch (Exception exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_readDependencyStateFailed.CurrentValue(), StateFilePath, Environment.NewLine, exception));
            return new PrerequisitesState();
        }
    }

    private static void SaveState(PrerequisitesState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
            File.WriteAllText(StateFilePath, JsonSerializer.Serialize(state));
        }
        catch (Exception exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_writeDependencyStateFailed.CurrentValue(), StateFilePath, Environment.NewLine, exception));
        }
    }

    private static bool IsDeveloperModeEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
        return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int value && value == 1;
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiQueryProductState(string productCode);

    private sealed class PrerequisitesState
    {
        public bool VcUwp { get; set; }
        public bool GameInput { get; set; }
        public DateTime? VcUwpFailedAt { get; set; }
        public DateTime? GameInputFailedAt { get; set; }
    }
}
