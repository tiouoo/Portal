using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Management.Deployment;
using Portal.Bedrock.Core;
using Portal.Bedrock.Core.Windows;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Portal.Localization;

namespace Portal.Bedrock;

public class BedrockLaunch : IBedrockLaunch
{
    private readonly BedrockInstanceConfig _instanceConfig;
    private ProcessMouseLocker? _mouseLocker;

    public BedrockLaunch(BedrockInstanceConfig instanceConfig)
    {
        _instanceConfig = instanceConfig;
    }

    public override async Task Launch(CancellationToken cancellationToken)
    {
        Log(BedrockLogLevel.Information, string.Format(LogLanguageManager.Instance.bedrockLaunch_preparingEnvironment.CurrentValue(), _instanceConfig.Name));
        BedrockWindowsPrerequisites.Validate(_instanceConfig);
        await BedrockWindowsPrerequisites.EnsureDependenciesAsync(_instanceConfig.InstancePath,
            _instanceConfig.BuildType, UpdateProgress, LogReceived, cancellationToken);
        var nativeLogPath = await BedrockDataIsolation.PrepareAsync(_instanceConfig, LogReceived,
            cancellationToken);
        Log(BedrockLogLevel.Information, LogLanguageManager.Instance.bedrockLaunch_environmentReady.CurrentValue());
        Process? launchedProcess = null;
        BedrockNativeLogMonitor.Start(nativeLogPath, () => launchedProcess, LogReceived);

        var options = new LaunchOptions
        {
            GameFolder = _instanceConfig.InstancePath,
            GameType = _instanceConfig.Type switch
            { 
                BedrockInstanceReleaseType.Preview => MinecraftGameTypeVersion.Preview,
                BedrockInstanceReleaseType.Release => MinecraftGameTypeVersion.Release,
                _ => throw new ArgumentOutOfRangeException(nameof(_instanceConfig.Type), _instanceConfig.Type, null)
            },
            MinecraftBuildType = _instanceConfig.BuildType switch
            {
                BedrockBuildType.GDK => MinecraftBuildTypeVersion.GDK,
                BedrockBuildType.UWP => MinecraftBuildTypeVersion.UWP,
                _ => throw new ArgumentOutOfRangeException(nameof(_instanceConfig.BuildType), _instanceConfig.BuildType, null)
            },
            RegisterProgress = new Progress<DeploymentProgress>(progress =>
            {
                Console.WriteLine($@"registerProcess_percent: {progress.percentage} - {progress.state}");
                Log(BedrockLogLevel.Debug, string.Format(LogLanguageManager.Instance.bedrockLaunch_registeringPackage.CurrentValue(), progress.state, progress.percentage));

                
                UpdateProgress?.Invoke(string.Format(CommonLanguageManager.Instance.bedrockLaunch_stepFormat.CurrentValue(), progress.state), progress.percentage);
            }),
            Progress = new Progress<LaunchState>(state =>
            {
                Console.WriteLine(state);
                Log(BedrockLogLevel.Information, string.Format(LogLanguageManager.Instance.bedrockLaunch_gameLaunchState.CurrentValue(), state));
                UpdateProgress?.Invoke(string.Format(CommonLanguageManager.Instance.bedrockLaunch_statusFormat.CurrentValue(), state), 0);

                
                if (state == LaunchState.Launched)
                {
                    UpdateProgress?.Invoke(CommonLanguageManager.Instance.bedrockLaunch_gameLaunchComplete.CurrentValue(), 100);
                }
            }),
            LaunchArgs = BuildLaunchArguments()
        };

        
        
        var existingProcessIds = Process.GetProcessesByName("Minecraft.Windows").Select(process => process.Id).ToHashSet();
        var launchStarted = DateTime.Now;
        Log(BedrockLogLevel.Information, string.Format(LogLanguageManager.Instance.bedrockLaunch_launchingMinecraftWindows.CurrentValue(), _instanceConfig.InstancePath));
        if (Authentication != null && _instanceConfig.BuildType == BedrockBuildType.GDK)
        {
            launchedProcess = await LaunchWithXboxAccountAsync(Authentication).ConfigureAwait(false);
        }
        else
        {
            launchedProcess = await Task.Run(() => new BedrockWindowsCore().LaunchGameAsync(options)).ConfigureAwait(false);
        }
        if (launchedProcess == null)
        {
            Log(BedrockLogLevel.Warning, LogLanguageManager.Instance.bedrockLaunch_waitingForProcess.CurrentValue());
            launchedProcess = await FindLaunchedProcessAsync(existingProcessIds, launchStarted).ConfigureAwait(false);
        }
        MinecraftProcess = launchedProcess ?? throw new InvalidOperationException(
            CommonLanguageManager.Instance.bedrockLaunch_processNotFoundAfterLaunch.CurrentValue());
        Log(BedrockLogLevel.Information, string.Format(LogLanguageManager.Instance.bedrockLaunch_processObtained.CurrentValue(), MinecraftProcess.Id));
        BedrockPreloadTrigger.Trigger(MinecraftProcess, LogReceived);
        try
        {
            if (_instanceConfig.EnableMouseLock &&
                (_instanceConfig.BuildType != BedrockBuildType.GDK || _instanceConfig.EnableMouseLockForGdk))
            {
                _mouseLocker = new ProcessMouseLocker(MinecraftProcess, _instanceConfig);
                MinecraftProcess.EnableRaisingEvents = true;
                MinecraftProcess.Exited += (_, _) => DisposeMouseLocker();
                Log(BedrockLogLevel.Information,
                    string.Format(LogLanguageManager.Instance.bedrockLaunch_mouseLockEnabled.CurrentValue(), _instanceConfig.MouseLockHotkey));
            }

            BedrockModInjector.Start(_instanceConfig, MinecraftProcess, LogReceived);
            LaunchFinish?.Invoke();
        }
        catch
        {
            DisposeMouseLocker();
            throw;
        }
    }

    private void Log(BedrockLogLevel level, string message) => LogReceived?.Invoke(message, level);

    private async Task<Process> LaunchWithXboxAccountAsync(BedrockAuthentication account)
    {
        Log(BedrockLogLevel.Information, string.Format(LogLanguageManager.Instance.bedrockLaunch_linkingXboxAccount.CurrentValue(), account.Gamertag));
        var launcher = new PortalXUserLauncher(_instanceConfig.InstancePath);
        launcher.DeployHook();
        using var authentication = await PortalXUserLauncher.AuthenticateAsync(account.AccessToken);
        var executable = Path.Combine(_instanceConfig.InstancePath, "Minecraft.Windows.exe");
        var result = await PortalXUserLauncher.LaunchAndInjectAsync(executable, BuildLaunchArguments(),
            _instanceConfig.InstancePath,
            authentication, TimeSpan.FromSeconds(60), processId =>
            {
                MinecraftProcess = Process.GetProcessById((int)processId);
                BedrockPreloadTrigger.Trigger(MinecraftProcess, LogReceived);
                ProcessStarted?.Invoke(MinecraftProcess);
            });
        var process = Process.GetProcessById((int)result.ProcessId);
        Log(BedrockLogLevel.Information, LogLanguageManager.Instance.bedrockLaunch_xboxAccountInjected.CurrentValue());
        return process;
    }

    private string? BuildLaunchArguments()
    {
        var arguments = _instanceConfig.LaunchArguments.Trim();
        if (_instanceConfig.EnableCreatorEditor)
            arguments = string.Join(' ', new[] { arguments, "minecraft://creator/?Editor=true" }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(arguments) ? null : arguments;
    }

    private void DisposeMouseLocker()
    {
        Interlocked.Exchange(ref _mouseLocker, null)?.Dispose();
    }

    private async Task<Process?> FindLaunchedProcessAsync(HashSet<int> existingProcessIds, DateTime launchStarted)
    {
        var executablePath = Path.GetFullPath(Path.Combine(_instanceConfig.InstancePath, "Minecraft.Windows.exe"));
        for (var attempt = 0; attempt < 80; attempt++)
        {
            foreach (var process in Process.GetProcessesByName("Minecraft.Windows")
                         .Where(process => !existingProcessIds.Contains(process.Id))
                         .OrderByDescending(GetStartTimeSafe))
            {
                var startTime = GetStartTimeSafe(process);
                if (startTime < launchStarted.AddSeconds(-1))
                    continue;
                var processPath = GetProcessPathSafe(process);
                if (processPath == null || string.Equals(Path.GetFullPath(processPath), executablePath,
                        StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        return null;
    }

    private static DateTime GetStartTimeSafe(Process process)
    {
        try { return process.StartTime; }
        catch (Exception exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_readStartTimeFailed.CurrentValue(), process.Id, Environment.NewLine, exception));
            return DateTime.MinValue;
        }
    }

    private static string? GetProcessPathSafe(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch (Exception exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrockLaunch_readProcessPathFailed.CurrentValue(), process.Id, Environment.NewLine, exception));
            return null;
        }
    }

    public override Process GetProcess()
    {
        return MinecraftProcess;
    }
}
