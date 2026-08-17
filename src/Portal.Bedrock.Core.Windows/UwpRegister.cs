using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace Portal.Bedrock.Core.Windows;

public class UwpRegister
{
	public static async Task<DeploymentResult> RegisterAppxAsync(DeploymentOptionsConfig config)
	{
		ValidateConfig(config);
		Uri packageUri = GetPackageUri(config.PackagePath);
		RemoveSignatureFile(GetPackageFolderPath(packageUri));
		return await ExecuteWithTimeout(new PackageManager().RegisterPackageAsync(packageUri, (IEnumerable<Uri>)null, ToCurrentUserDevelopmentOptions(config.DeploymentOptions)), config);
	}

	public static async Task<DeploymentResult> AddAppxAsync(DeploymentOptionsConfig config)
	{
		ValidateConfig(config);
		Uri packageUri = GetPackageUri(config.PackagePath);
		RemoveSignatureFile(GetPackageFolderPath(packageUri));
		return await ExecuteWithTimeout(new PackageManager().AddPackageAsync(packageUri, (IEnumerable<Uri>)null, ToCurrentUserDevelopmentOptions(config.DeploymentOptions)), config);
	}

	private static Uri GetPackageUri(string packagePath)
	{
		if (Uri.TryCreate(packagePath, UriKind.Absolute, out Uri result) && result.IsFile)
		{
			return result;
		}
		return new Uri(Path.GetFullPath(packagePath));
	}

	private static string GetPackageFolderPath(Uri packageUri)
	{
		string localPath = packageUri.LocalPath;
		if (!File.Exists(localPath))
		{
			return localPath;
		}
		return Path.GetDirectoryName(localPath) ?? localPath;
	}

	private static DeploymentOptions ToCurrentUserDevelopmentOptions(DeploymentOptions options)
	{
		return (DeploymentOptions)((int)options | 2);
	}

	private static void RemoveSignatureFile(string packageFolderPath)
	{
		try
		{
			string path = Path.Combine(packageFolderPath, "AppxSignature.p7x");
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("Error removing signature file: " + ex.Message);
		}
	}

	private static async Task<DeploymentResult> ExecuteWithTimeout(IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> asyncOperation, DeploymentOptionsConfig config)
	{
		if (config.Timeout.HasValue)
		{
			using (CancellationTokenSource timeoutCts = new CancellationTokenSource(config.Timeout.Value))
			{
				using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(config.CancellationToken, timeoutCts.Token);
				return await WindowsRuntimeSystemExtensions.AsTask<DeploymentResult, DeploymentProgress>(asyncOperation, linkedCts.Token, config.ProgressCallback);
			}
		}
		return await WindowsRuntimeSystemExtensions.AsTask<DeploymentResult, DeploymentProgress>(asyncOperation, config.CancellationToken, config.ProgressCallback);
	}

	public static bool CheckForPackageVersion(string packageName, string version)
	{
		foreach (Package item in new PackageManager().FindPackagesForUser(string.Empty))
		{
			if (item.Id.Name == packageName && $"{item.Id.Version.Major}.{item.Id.Version.Minor}.{item.Id.Version.Build}.{item.Id.Version.Revision}" == version)
			{
				return true;
			}
		}
		return false;
	}

	public static bool IsPackageInstalled(string packageName)
	{
		if (string.IsNullOrWhiteSpace(packageName))
		{
			throw new ArgumentException("Package name cannot be null or empty", "packageName");
		}
		foreach (Package item in new PackageManager().FindPackagesForUser(string.Empty))
		{
			if (item.Id.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static void ValidateConfig(DeploymentOptionsConfig config)
	{
		if (string.IsNullOrWhiteSpace(config.PackagePath))
		{
			throw new ArgumentException("Package path cannot be null or empty", "config");
		}
		if ((!Uri.TryCreate(config.PackagePath, UriKind.Absolute, out Uri result) || !result.IsFile) && !Path.IsPathFullyQualified(config.PackagePath))
		{
			throw new ArgumentException("Package path must be an absolute file path or file URI", "config");
		}
	}
}
