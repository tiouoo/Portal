using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Portal.Bedrock.Standard.Interface;

public abstract class IBedrockLaunch
{
    public abstract Task Launch();
    public abstract Process GetProcess();
    public Process MinecraftProcess;
    public Action<string,double> UpdateProgress;
    public Action? LaunchFinish;
}