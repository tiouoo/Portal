using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Bedrock.Standard.Interface;

public abstract class IBedrockLaunch
{
    public BedrockAuthentication? Authentication { get; set; }
    public abstract Task Launch(CancellationToken cancellationToken);
    public abstract Process GetProcess();
    public Process MinecraftProcess;
    public Action<string, double?>? UpdateProgress;
    public Action<string, BedrockLogLevel>? LogReceived;
    public Action? LaunchFinish;
}

public enum BedrockLogLevel
{
    Debug,
    Information,
    Warning,
    Error
}
