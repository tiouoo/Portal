using Windows.Management.Deployment;

namespace Portal.Bedrock.Core.Windows;

public class WindowsInstallResult : InstallResult
{
	public DeploymentResult? DeploymentResult { get; set; }
}
