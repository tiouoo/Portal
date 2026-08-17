using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Windows.Management.Deployment;

namespace Portal.Bedrock.Core.Windows;

public class DeploymentOptionsConfig
{
	[CompilerGenerated]
	[DebuggerBrowsable(DebuggerBrowsableState.Never)]
	private DeploymentOptions _003CDeploymentOptions_003Ek__BackingField;

	public string PackagePath { get; set; } = string.Empty;

	public DeploymentOptions DeploymentOptions
	{
		[CompilerGenerated]
		get
		{
			return _003CDeploymentOptions_003Ek__BackingField;
		}
		[CompilerGenerated]
		set
		{
			_003CDeploymentOptions_003Ek__BackingField = value;
		}
	}

	public CancellationToken CancellationToken { get; set; }

	public IProgress<DeploymentProgress>? ProgressCallback { get; set; }

	public TimeSpan? Timeout { get; set; }
}
