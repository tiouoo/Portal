using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Management.Deployment;
using BedrockLauncher.Core;
using BedrockLauncher.Core.CoreOption;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;

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
        Log(BedrockLogLevel.Information, $"开始准备实例 {_instanceConfig.Name} 的基岩版启动环境");
        BedrockWindowsPrerequisites.Validate(_instanceConfig);
        await BedrockWindowsPrerequisites.EnsureDependenciesAsync(_instanceConfig.InstancePath,
            _instanceConfig.BuildType, UpdateProgress, LogReceived, cancellationToken);
        var nativeLogPath = BedrockDataIsolation.Prepare(_instanceConfig, LogReceived);
        Log(BedrockLogLevel.Information, "基岩版数据隔离和预加载环境准备完成");
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
                Log(BedrockLogLevel.Debug, $"注册游戏包：{progress.state}，进度 {progress.percentage}%");

                
                UpdateProgress?.Invoke($"步骤：{progress.state}", progress.percentage);
            }),
            Progress = new Progress<LaunchState>(state =>
            {
                Console.WriteLine(state);
                Log(BedrockLogLevel.Information, $"游戏启动状态：{state}");
                UpdateProgress?.Invoke($"状态：{state}", 0);

                
                if (state == LaunchState.Launched)
                {
                    UpdateProgress?.Invoke("状态：游戏启动完成，开始计时", 100);
                }
            }),
            LaunchArgs = BuildLaunchArguments()
        };

        
        
        var existingProcessIds = Process.GetProcessesByName("Minecraft.Windows").Select(process => process.Id).ToHashSet();
        var launchStarted = DateTime.Now;
        Log(BedrockLogLevel.Information, $"启动 Minecraft.Windows，实例目录：{_instanceConfig.InstancePath}");
        if (Authentication != null && _instanceConfig.BuildType == BedrockBuildType.GDK)
        {
            launchedProcess = await LaunchWithXboxAccountAsync(Authentication).ConfigureAwait(false);
        }
        else
        {
            launchedProcess = await Task.Run(() => new BedrockCore().LaunchGameAsync(options)).ConfigureAwait(false);
        }
        if (launchedProcess == null)
        {
            Log(BedrockLogLevel.Warning, "BedrockLauncher.Core 未在 5 秒内返回 Minecraft 进程，继续等待进程启动");
            launchedProcess = await FindLaunchedProcessAsync(existingProcessIds, launchStarted).ConfigureAwait(false);
        }
        MinecraftProcess = launchedProcess ?? throw new InvalidOperationException(
            "基岩版启动状态已完成，但未找到 Minecraft 进程。游戏可能在启动时提前退出。");
        Log(BedrockLogLevel.Information, $"已获取 Minecraft 进程，PID：{MinecraftProcess.Id}");
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
                    $"Windows 鼠标锁定已启用，解锁热键：{_instanceConfig.MouseLockHotkey}");
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
        Log(BedrockLogLevel.Information, $"正在关联 Xbox 账户 {account.Gamertag}");
        var launcher = new PortalXUserLauncher(_instanceConfig.InstancePath);
        launcher.DeployHook();
        using var authentication = await PortalXUserLauncher.AuthenticateAsync(account.AccessToken);
        var executable = Path.Combine(_instanceConfig.InstancePath, "Minecraft.Windows.exe");
        var result = await PortalXUserLauncher.LaunchAndInjectAsync(executable, BuildLaunchArguments(),
            _instanceConfig.InstancePath,
            authentication, TimeSpan.FromSeconds(60), processId =>
            {
                MinecraftProcess = Process.GetProcessById((int)processId);
                ProcessStarted?.Invoke(MinecraftProcess);
            });
        var process = Process.GetProcessById((int)result.ProcessId);
        Log(BedrockLogLevel.Information, "Xbox 账户已注入基岩版游戏进程");
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
            Trace.TraceError($"读取 Minecraft 进程启动时间失败：{process.Id}{Environment.NewLine}{exception}");
            return DateTime.MinValue;
        }
    }

    private static string? GetProcessPathSafe(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch (Exception exception)
        {
            Trace.TraceError($"读取 Minecraft 进程路径失败：{process.Id}{Environment.NewLine}{exception}");
            return null;
        }
    }

    public override Process GetProcess()
    {
        return MinecraftProcess;
    }
}
