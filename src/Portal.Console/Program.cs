using Portal.Bedrock;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Bedrock;

var manager = InstanceManager.Instance;
manager.RefreshAll([(@"D:\Games\.minecraft", ".minecraft")]);
foreach (var x in manager.Instances)
{
    Console.WriteLine($"{x.Type} {x.VersionId}");
    if (x.BedrockConfig is { } bedrockConf)
    {
        Console.WriteLine($"Bedrock Config: Name={bedrockConf.Name}, Version={bedrockConf.Version}, BuildType={bedrockConf.BuildType}, Type={bedrockConf.Type}");
    }
}

var launcher = new BedrockLaunch(BedrockHelper.GetInstanceConfig(@"D:\Games\.minecraft\bedrock_versions\1.26.3202"));
await launcher.Launch();

var process = launcher.GetProcess();
Console.WriteLine(process.Id);

process.WaitForExit();
Console.WriteLine(process.ExitCode);
