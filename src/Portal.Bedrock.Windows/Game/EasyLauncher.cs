using System.Diagnostics;
using Portal.Base.Config;
using Portal.Base.Interface;

namespace Portal.Bedrock.Windows.Game;

public class EasyLauncher : ILauncher
{
    private readonly InstanceConfig _instanceConfig;

    public EasyLauncher(InstanceConfig instanceConfig)
    {
        _instanceConfig = instanceConfig;
    }

    public Process Launch()
    {
        return null;
    }
}