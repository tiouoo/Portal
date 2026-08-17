using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using Windows.System;

namespace Portal.Bedrock.Core.Windows;

public sealed class BedrockWindowsCore : BedrockCore
{
	public CoreOptions Options { get; set; } = new CoreOptions();

	public override async Task<InstallResult?> InstallPackageAsync(LocalGamePackageOptions options)
	{
		if (options.Type == MinecraftBuildTypeVersion.GDK)
		{
			return await base.InstallPackageAsync(options);
		}
		if (options.Type != MinecraftBuildTypeVersion.UWP)
		{
			return null;
		}
		WindowsInstallResult installResult = new WindowsInstallResult();
		options.InstallStates?.Report(InstallStates.Extracting);
		await ZipExtractor.ExtractWithProgressAsync(options.FileFullPath, options.InstallDstFolder, options.ExtractionProgress, options.CancellationToken ?? CancellationToken.None);
		File.Delete(Path.Combine(options.InstallDstFolder, "AppxSignature.p7x"));
		options.InstallStates?.Report(InstallStates.Extracted);
		MinecraftGameTypeVersion gameTypeVersion = options.GameTypeVersion;
		string packageName = gameTypeVersion switch
		{
			MinecraftGameTypeVersion.Beta => "Microsoft.MinecraftWindowsBeta", 
			MinecraftGameTypeVersion.Release => "Microsoft.MinecraftUWP", 
			MinecraftGameTypeVersion.Preview => "Microsoft.MinecraftWindowsBeta", 
			_ => "Microsoft.MinecraftUWP", 
		};
		bool isInstalled = UwpRegister.IsPackageInstalled(packageName);
		await ManifestEditor.EditManifest(options.InstallDstFolder, options.GameName ?? TimeBasedVersion.GetVersion(), options.BackGroundConfig);
		DeploymentOptionsConfig config = new DeploymentOptionsConfig
		{
			PackagePath = Path.Combine(options.InstallDstFolder, "AppxManifest.xml"),
			CancellationToken = (options.CancellationToken ?? CancellationToken.None),
			Timeout = TimeSpan.FromMinutes(3L),
			DeploymentOptions = (DeploymentOptions)(isInstalled ? 262146 : 2),
			ProgressCallback = ((options is WindowsLocalGamePackageOptions windows) ? windows.DeployProgress : null)
		};
		options.InstallStates?.Report(InstallStates.Registering);
		DeploymentResult deploymentResult = await UwpRegister.RegisterAppxAsync(config);
		options.InstallStates?.Report(InstallStates.Registered);
		installResult.DeploymentResult = deploymentResult;
		return installResult;
	}

	public async Task<Process> LaunchGameAsync(LaunchOptions options)
	{
		if (options.MinecraftBuildType == MinecraftBuildTypeVersion.GDK)
		{
			options.Progress?.Report(LaunchState.Launching);
			string executable = Path.Combine(options.GameFolder, "Minecraft.Windows.exe");
			string processName = Path.GetFileNameWithoutExtension("Minecraft.Windows.exe");
			Dictionary<int, DateTime> existing = Process.GetProcessesByName(processName).ToDictionary((Process p) => p.Id, (Process p) => GetStartTimeSafe(p));
			DateTime now = DateTime.Now;
			string arguments = "/c start \"\" \"" + executable + "\" " + options.LaunchArgs;
			Process.Start(new ProcessStartInfo
			{
				FileName = "cmd.exe",
				Arguments = arguments,
				UseShellExecute = false,
				CreateNoWindow = true
			});
			Process launched = null;
			for (int attempt = 0; attempt < 10; attempt++)
			{
				Process[] processesByName = Process.GetProcessesByName(processName);
				foreach (Process process in processesByName)
				{
					if (!existing.ContainsKey(process.Id))
					{
						DateTime startTime = GetStartTimeSafe(process);
						if (startTime != DateTime.MinValue && startTime >= now.AddMilliseconds(-200.0))
						{
							launched = process;
							break;
						}
					}
				}
				if (launched != null)
				{
					break;
				}
				Thread.Sleep(500);
			}
			Process result = launched;
			options.Progress?.Report(LaunchState.Launched);
			return result;
		}
		if (options.MinecraftBuildType == MinecraftBuildTypeVersion.UWP)
		{
			string manifestPath = Path.Combine(options.GameFolder, "AppxManifest.xml");
			MinecraftGameTypeVersion gameType = options.GameType;
			string text = gameType switch
			{
				MinecraftGameTypeVersion.Release => "Microsoft.MinecraftUWP_8wekyb3d8bbwe", 
				MinecraftGameTypeVersion.Preview => "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", 
				MinecraftGameTypeVersion.Beta => "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", 
				_ => throw new ArgumentOutOfRangeException("GameType"), 
			};
			string packageFamily = text;
			MinecraftGameTypeVersion gameType2 = options.GameType;
			text = gameType2 switch
			{
				MinecraftGameTypeVersion.Beta => "Microsoft.MinecraftWindowsBeta", 
				MinecraftGameTypeVersion.Release => "Microsoft.MinecraftUWP", 
				MinecraftGameTypeVersion.Preview => "Microsoft.MinecraftWindowsBeta", 
				_ => throw new ArgumentOutOfRangeException("GameType"), 
			};
			string packageName = text;
			if (!File.Exists(manifestPath))
			{
				throw new IOException("File doesn't exist");
			}
			PackageManager packageManager = new PackageManager();
			bool alreadyRegistered = false;
			foreach (Package item in packageManager.FindPackagesForUser(string.Empty))
			{
				if (item.Id.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase) && Path.GetFullPath(item.InstalledPath) == Path.GetFullPath(options.GameFolder))
				{
					alreadyRegistered = true;
				}
			}
			bool isInstalled = UwpRegister.IsPackageInstalled(packageName);
			DeploymentOptionsConfig config = new DeploymentOptionsConfig();
			options.Progress?.Report(LaunchState.Registering);
			config.CancellationToken = options.CancellationToken.GetValueOrDefault();
			config.PackagePath = manifestPath;
			config.DeploymentOptions = (DeploymentOptions)(isInstalled ? 262146 : 2);
			config.ProgressCallback = options.RegisterProgress;
			if ((!alreadyRegistered & isInstalled) || !isInstalled)
			{
				DeploymentResult registration = await UwpRegister.RegisterAppxAsync(config);
				options.Progress?.Report(LaunchState.Registered);
				if (!registration.IsRegistered)
				{
					throw new Exception(registration.ErrorText);
				}
			}
			if (options.Old_VersionLaunching)
			{
				IList<AppDiagnosticInfo> diagnostics = await WindowsRuntimeSystemExtensions.AsTask<IList<AppDiagnosticInfo>>(AppDiagnosticInfo.RequestInfoForPackageAsync(packageFamily));
				if (diagnostics.Count != 0)
				{
					await WindowsRuntimeSystemExtensions.AsTask<AppActivationResult>(diagnostics[0].LaunchAsync());
				}
			}
			else
			{
				LauncherOptions launcherOptions = new LauncherOptions
				{
					TargetApplicationPackageFamilyName = packageFamily
				};
				string uriString = BuildLaunchUri(options.LaunchArgs);
				await WindowsRuntimeSystemExtensions.AsTask<bool>(Launcher.LaunchUriAsync(new Uri(uriString), launcherOptions));
			}
			IEnumerable<Process> candidates = Process.GetProcessesByName("Minecraft.Windows").Concat(Process.GetProcessesByName("Minecraft.Win10.DX11"));
			return candidates.OrderBy((Process p) => p.StartTime).Last();
		}
		throw new PlatformNotSupportedException($"Unsupported build type: {options.MinecraftBuildType}");
	}

	private static string BuildLaunchUri(string? launchArgs)
	{
		string text = "minecraft://launch";
		if (string.IsNullOrEmpty(launchArgs))
		{
			return text;
		}
		string text2;
		if (launchArgs.StartsWith("minecraft://", StringComparison.OrdinalIgnoreCase))
		{
			int num = launchArgs.IndexOf('?');
			if (num >= 0 && num < launchArgs.Length - 1)
			{
				text2 = launchArgs.Substring(num + 1);
			}
			else
			{
				string text3 = launchArgs.Substring("minecraft://".Length).TrimStart('/');
				text2 = (string.IsNullOrEmpty(text3) ? string.Empty : (text3 + "=true"));
			}
		}
		else if (launchArgs.Contains('=') && !launchArgs.Contains(' '))
		{
			text2 = launchArgs;
		}
		else if (launchArgs.Contains('=') && launchArgs.Contains(' '))
		{
			string[] array = (from arg in launchArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				where arg.Contains('=')
				select arg.Trim()).ToArray();
			text2 = ((array.Length == 0) ? ("args=" + Uri.EscapeDataString(launchArgs)) : string.Join("&", array));
		}
		else
		{
			text2 = "args=" + Uri.EscapeDataString(launchArgs);
		}
		return string.IsNullOrEmpty(text2) ? text : (text + "?" + text2);
	}

	public async Task<DeploymentResult?> RemoveUWPGameAsync(MinecraftGameTypeVersion type)
	{
		PackageManager packageManager = new PackageManager();
		foreach (Package item in packageManager.FindPackagesForUser(string.Empty))
		{
			string text = type switch
			{
				MinecraftGameTypeVersion.Release => "Microsoft.MinecraftUWP_8wekyb3d8bbwe", 
				MinecraftGameTypeVersion.Preview => "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", 
				MinecraftGameTypeVersion.Beta => "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe", 
				_ => throw new ArgumentOutOfRangeException("type"), 
			};
			string familyName = text;
			if (item.Id.FamilyName == familyName)
			{
				return await WindowsRuntimeSystemExtensions.AsTask<DeploymentResult, DeploymentProgress>(packageManager.RemovePackageAsync(item.Id.FullName, (RemovalOptions)4096));
			}
		}
		return null;
	}

	public (bool, bool) IsHasVCRuntime(Architecture arch)
	{
		try
		{
			string[] registryPaths = arch switch
			{
				Architecture.X64 => new string[2] { "SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64", "SOFTWARE\\WOW6432Node\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64" }, 
				Architecture.X86 => new string[2] { "SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x86", "SOFTWARE\\WOW6432Node\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x86" }, 
				Architecture.Arm64 => new string[2] { "SOFTWARE\\WOW6432Node\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\arm64", "SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\arm64" }, 
				_ => new string[2] { "SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64", "SOFTWARE\\WOW6432Node\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64" }, 
			};
			bool item = CheckWin32Version(registryPaths);
			bool item2 = new PackageManager().FindPackagesForUser(string.Empty).Any((Package package) => package.Id.Name.Contains("Microsoft.VCLibs.140", StringComparison.OrdinalIgnoreCase));
			return (item2, item);
		}
		catch
		{
			return (false, false);
		}
	}

	public async Task AutoCompleteGameInput()
	{
		if (!MsiHelper.IsMsiProductInstalledByGuid("64d0ccb1-329e-d507-0886-47e53d59ae21"))
		{
			await VCRuntimeHelper.InstallGameInput();
		}
	}

	public async Task InitAsync()
	{
		if (Options.IsAutoOpenDevelopment && !GetWindowsDevelopmentState())
		{
			throw new BedrockCoreException("Windows Developer Mode is required for non-admin UWP loose package registration. Please enable Developer Mode in Windows settings.");
		}
		if (Options.IsAutoCompleteVC && !IsHasVCRuntime(RuntimeInformation.OSArchitecture).Item2)
		{
			await VCRuntimeHelper.CompleteVCRuntimeAsync(RuntimeInformation.OSArchitecture);
		}
		if (Options.IsAutoCompleteGameInput)
		{
			await AutoCompleteGameInput();
		}
	}

	public bool GetWindowsDevelopmentState()
	{
		try
		{
			return Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock", writable: true)?.GetValue("AllowDevelopmentWithoutDevLicense", 1) is int num && num != 0;
		}
		catch
		{
			throw new BedrockCoreException("Can't Get Development state");
		}
	}

	public bool OpenWindowsDevelopment()
	{
		try
		{
			Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\AppModelUnlock", writable: true)?.SetValue("AllowDevelopmentWithoutDevLicense", 1);
			return true;
		}
		catch
		{
			throw new BedrockCoreException("Can't Open Deveopment Successfully");
		}
	}

	private static bool CheckWin32Version(string[] registryPaths)
	{
		foreach (string name in registryPaths)
		{
			using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey(name);
			if (registryKey != null)
			{
				return true;
			}
		}
		return false;
	}

	private static DateTime GetStartTimeSafe(Process process)
	{
		try
		{
			return process.StartTime;
		}
		catch
		{
			return DateTime.MinValue;
		}
	}
}
