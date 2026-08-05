using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BedrockLauncher.Core;
using BedrockLauncher.Core.CoreOption;
using BedrockLauncher.Core.DependsComplete;
using BedrockLauncher.Core.UwpRegister;
using BedrockLauncher.Core.Utils;
using Microsoft.Win32;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Windows.Management.Deployment;

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

    public static async Task EnsureDependenciesAsync(string instancePath, BedrockBuildType buildType,
        Action<string, double?>? progress = null,
        Action<string, BedrockLogLevel>? log = null,
        CancellationToken cancellationToken = default)
    {
        var state = LoadState();
        log?.Invoke($"开始检查基岩版依赖：实例 {instancePath}，构建类型 {buildType}", BedrockLogLevel.Information);
        var architecture = RuntimeInformation.OSArchitecture;
        var core = new BedrockCore();
        var (hasVcUwp, hasVcWin32) = core.IsHasVCRuntime(architecture);

        if (!hasVcWin32)
        {
            progress?.Invoke("正在检查基岩版系统运行环境…", null);
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
                log?.Invoke("UWP VC++ 运行库上次安装失败，本次启动已跳过", BedrockLogLevel.Warning);
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
                log?.Invoke("GameInput 运行组件上次安装失败，本次启动已跳过", BedrockLogLevel.Warning);
            }
        }
        else
        {
            state.GameInput = true;
            state.GameInputFailedAt = null;
        }

        SaveState(state);
        log?.Invoke("基岩版依赖检查完成", BedrockLogLevel.Information);
        progress?.Invoke("系统运行环境检查完成", 100);
    }

    private static async Task EnsureVcWin32Async(Architecture architecture, Action<string, double?>? progress,
        Action<string, BedrockLogLevel>? log, CancellationToken cancellationToken)
    {
        log?.Invoke("检测到缺少 Microsoft Visual C++ 2015-2022 运行库，正在安装…", BedrockLogLevel.Information);
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
                $"VC++ 运行库安装失败（退出码 {exitCode}）。请手动安装 Microsoft Visual C++ 2015-2022 运行库后重试。");
        log?.Invoke("Microsoft Visual C++ 2015-2022 运行库安装完成", BedrockLogLevel.Information);
    }

    private static async Task EnsureVcUwpAsync(Architecture architecture, PrerequisitesState state,
        Action<string, double?>? progress, Action<string, BedrockLogLevel>? log, CancellationToken cancellationToken)
    {
        log?.Invoke("检测到缺少 UWP VC++ 运行库组件，正在安装…", BedrockLogLevel.Information);
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
                    p => progress?.Invoke($"正在下载 UWP VC++ 运行库组件… ({p:F0}%)", p))
                .ConfigureAwait(false);
            progress?.Invoke("正在安装 UWP VC++ 运行库组件…", null);
            var result = await UwpRegister.AddAppxAsync(new DeploymentOptionsConfig
            {
                PackagePath = appxPath,
                DeploymentOptions = DeploymentOptions.ForceApplicationShutdown
            }).ConfigureAwait(false);
            if (result.IsRegistered && HasVcUwpRuntime())
            {
                state.VcUwp = true;
                state.VcUwpFailedAt = null;
                log?.Invoke("UWP VC++ 运行库组件安装完成", BedrockLogLevel.Information);
            }
            else
            {
                state.VcUwpFailedAt = DateTime.UtcNow;
                log?.Invoke($"UWP VC++ 运行库安装失败：{result.ErrorText}", BedrockLogLevel.Warning);
            }
        }
        catch (Exception exception)
        {
            state.VcUwpFailedAt = DateTime.UtcNow;
            Trace.TraceError($"UWP VC++ 运行库安装异常。{Environment.NewLine}{exception}");
            log?.Invoke($"UWP VC++ 运行库安装异常：{exception}", BedrockLogLevel.Warning);
        }
    }

    private static async Task EnsureGameInputAsync(string instancePath, PrerequisitesState state,
        Action<string, double?>? progress, Action<string, BedrockLogLevel>? log, CancellationToken cancellationToken)
    {
        log?.Invoke("检测到缺少 GameInput 运行组件，正在安装…", BedrockLogLevel.Information);
        progress?.Invoke("正在安装 GameInput 运行组件…", null);
        var bundled = Path.Combine(instancePath, "Installers", "GameInputRedist.msi");
        if (File.Exists(bundled))
        {
            var exitCode = await RunElevatedAsync("msiexec.exe", $"/i \"{bundled}\" /qn /norestart")
                .ConfigureAwait(false);
            if (exitCode is 0 or 3010 or 3019)
            {
                MarkGameInputInstalled(state);
                log?.Invoke("GameInput 运行组件安装完成", BedrockLogLevel.Information);
                return;
            }

            log?.Invoke($"使用游戏目录内的 GameInput 安装程序失败（退出码 {exitCode}），正在尝试在线安装…",
                BedrockLogLevel.Warning);
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), "Portal", "GameInput");
        Directory.CreateDirectory(tempFolder);
        var msiPath = Path.Combine(tempFolder, "GameInputRedist.msi");
        try
        {
            await DownloadFileAsync(VCRuntimeHelper.VCUri.GameInputRedist, msiPath, cancellationToken,
                    p => progress?.Invoke($"正在下载 GameInput 运行组件… ({p:F0}%)", p))
                .ConfigureAwait(false);
            progress?.Invoke("正在安装 GameInput 运行组件…", null);
            var exitCode = await RunElevatedAsync("msiexec.exe", $"/i \"{msiPath}\" /qn /norestart")
                .ConfigureAwait(false);
            if (exitCode is 0 or 3010 or 3019)
            {
                MarkGameInputInstalled(state);
                log?.Invoke("GameInput 运行组件安装完成", BedrockLogLevel.Information);
            }
            else
            {
                state.GameInputFailedAt = DateTime.UtcNow;
                log?.Invoke($"GameInput 运行组件安装失败（退出码 {exitCode}）。", BedrockLogLevel.Warning);
            }
        }
        catch (Exception exception)
        {
            state.GameInputFailedAt = DateTime.UtcNow;
            Trace.TraceError($"GameInput 运行组件安装失败。{Environment.NewLine}{exception}");
            log?.Invoke($"GameInput 运行组件安装失败：{exception}", BedrockLogLevel.Warning);
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
                        p => progress?.Invoke($"正在下载 Microsoft Visual C++ 2015-2022 运行库… ({p:F0}%)", p))
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lastError = exception;
                Trace.TraceError($"下载 VC++ 运行库失败：{url}{Environment.NewLine}{exception}");
                continue;
            }

            progress?.Invoke("正在安装 Microsoft Visual C++ 2015-2022 运行库…", null);
            return await RunElevatedAsync(vcPath, "/install /quiet /norestart").ConfigureAwait(false);
        }

        throw new InvalidOperationException("VC++ 运行库下载失败，请检查网络连接后重试。", lastError);
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
            Trace.TraceError($"检查 GameInput 安装状态失败。{Environment.NewLine}{exception}");
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
            Trace.TraceError($"检查 UWP VC++ 运行库状态失败。{Environment.NewLine}{exception}");
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
            throw new InvalidOperationException("用户取消了管理员权限授权，依赖安装未完成。");
        }
    }

    private static async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken,
        Action<double>? onProgress = null)
    {
        using var client = new HttpClient();
        Trace.TraceInformation($"下载基岩版运行依赖：{url} -> {path}。");
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
            Trace.TraceError($"读取基岩版依赖状态失败：{StateFilePath}{Environment.NewLine}{exception}");
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
            Trace.TraceError($"写入基岩版依赖状态失败：{StateFilePath}{Environment.NewLine}{exception}");
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
