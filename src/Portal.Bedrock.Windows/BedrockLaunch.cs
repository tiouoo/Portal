using System;
using System.Diagnostics;
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
        BedrockDataIsolation.Prepare(_instanceConfig);

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

                // 使用回调更新进度，而不是直接操作 UI
                UpdateProgress?.Invoke($"步骤：{progress.state}", progress.percentage);
            }),
            Progress = new Progress<LaunchState>(state =>
            {
                Console.WriteLine(state);
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
        MinecraftProcess = await Task.Run(() => new BedrockCore().LaunchGameAsync(options)).ConfigureAwait(false);
        
        LaunchFinish?.Invoke();
    }

    public override Process GetProcess()
    {
        return MinecraftProcess;
    }
}
