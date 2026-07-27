using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    public BedrockLaunch(BedrockInstanceConfig instanceConfig)
    {
        _instanceConfig = instanceConfig;
    }

    public override async Task Launch()
    {
        Log(BedrockLogLevel.Information, $"开始准备实例 {_instanceConfig.Name} 的基岩版启动环境");
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

                // 使用回调更新进度，而不是直接操作 UI
                UpdateProgress?.Invoke($"步骤：{progress.state}", progress.percentage);
            }),
            Progress = new Progress<LaunchState>(state =>
            {
                Console.WriteLine(state);
                Log(BedrockLogLevel.Information, $"游戏启动状态：{state}");
                UpdateProgress?.Invoke($"状态：{state}", 0);

                // 当游戏启动状态变化时，更新进度文本
                if (state == LaunchState.Launched)
                {
                    UpdateProgress?.Invoke("状态：游戏启动完成，开始计时", 100);
                }
            }),
            LaunchArgs = null
        };

        // Package registration can perform synchronous work before its task is returned.
        // Keep it off Avalonia's UI thread so the task drawer remains responsive.
        var existingProcessIds = Process.GetProcessesByName("Minecraft.Windows").Select(process => process.Id).ToHashSet();
        var launchStarted = DateTime.Now;
        launchedProcess = await Task.Run(() => new BedrockCore().LaunchGameAsync(options)).ConfigureAwait(false);
        if (launchedProcess == null)
        {
            Log(BedrockLogLevel.Warning, "BedrockLauncher.Core 未在 5 秒内返回 Minecraft 进程，继续等待进程启动");
            launchedProcess = await FindLaunchedProcessAsync(existingProcessIds, launchStarted).ConfigureAwait(false);
        }
        MinecraftProcess = launchedProcess ?? throw new InvalidOperationException(
            "基岩版启动状态已完成，但未找到 Minecraft 进程。游戏可能在启动时提前退出。");
        Log(BedrockLogLevel.Information, $"已获取 Minecraft 进程，PID：{MinecraftProcess.Id}");
        BedrockModInjector.Start(_instanceConfig, MinecraftProcess, LogReceived);
        
        LaunchFinish?.Invoke();
    }

    private void Log(BedrockLogLevel level, string message) => LogReceived?.Invoke(message, level);

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
        catch { return DateTime.MinValue; }
    }

    private static string? GetProcessPathSafe(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    public override Process GetProcess()
    {
        return MinecraftProcess;
    }
}
